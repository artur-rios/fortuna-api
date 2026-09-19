using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Shared.Attachments;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Attachments;

public sealed class EfAttachmentLifecycleStore(
    AppDbContext context,
    IAttachmentStore storage) : IAttachmentLifecycleStore
{
    public async Task<AttachmentLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid attachmentId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var attachment = await FindOwnedAsync(userId, attachmentId, cancellationToken);
        if (attachment is null)
        {
            return new AttachmentLifecycleResult(AttachmentLifecycleOutcome.NotFound);
        }

        attachment.SoftDelete(changedAt);
        await context.SaveChangesAsync(cancellationToken);

        return new AttachmentLifecycleResult(
            AttachmentLifecycleOutcome.Succeeded,
            attachment.PublicId,
            attachment.IsDeleted);
    }

    public async Task<AttachmentLifecycleResult> HardDeleteAsync(
        Guid userId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await FindOwnedAsync(userId, attachmentId, cancellationToken);
        if (attachment is null)
        {
            return new AttachmentLifecycleResult(AttachmentLifecycleOutcome.NotFound);
        }

        if (!attachment.IsDeleted)
        {
            return new AttachmentLifecycleResult(
                AttachmentLifecycleOutcome.HardDeleteRequiresSoftDeletion);
        }

        if (!await StorageIsHealthyAsync(cancellationToken))
        {
            return new AttachmentLifecycleResult(AttachmentLifecycleOutcome.StorageUnavailable);
        }

        context.Attachments.Remove(attachment);
        await context.SaveChangesAsync(cancellationToken);
        await DeleteObjectsAsync([attachment.StorageKey], CancellationToken.None);

        return new AttachmentLifecycleResult(
            AttachmentLifecycleOutcome.Succeeded,
            attachment.PublicId,
            false);
    }

    public async Task SoftDeleteForTransactionsAsync(
        IReadOnlyDictionary<long, Guid> transactionCascadeIds,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        if (transactionCascadeIds.Count == 0)
        {
            return;
        }

        var transactionIds = transactionCascadeIds.Keys.ToArray();
        var attachments = await context.Attachments
            .Where(attachment => transactionIds.Contains(attachment.TransactionId))
            .ToListAsync(cancellationToken);
        foreach (var attachment in attachments.Where(item => !item.IsDeleted))
        {
            attachment.SoftDeleteFromCascade(
                transactionCascadeIds[attachment.TransactionId],
                changedAt);
        }
    }

    public async Task RestoreForTransactionsAsync(
        IReadOnlyDictionary<long, Guid> transactionCascadeIds,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        if (transactionCascadeIds.Count == 0)
        {
            return;
        }

        var transactionIds = transactionCascadeIds.Keys.ToArray();
        var attachments = await context.Attachments
            .Where(attachment => transactionIds.Contains(attachment.TransactionId))
            .ToListAsync(cancellationToken);
        foreach (var attachment in attachments)
        {
            attachment.RestoreFromCascade(
                transactionCascadeIds[attachment.TransactionId],
                changedAt);
        }
    }

    public async Task<AttachmentRemoval> RemoveForTransactionsAsync(
        IReadOnlyCollection<long> transactionIds,
        CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
        {
            return AttachmentRemoval.Empty;
        }

        var attachments = await context.Attachments
            .Where(attachment => transactionIds.Contains(attachment.TransactionId))
            .ToListAsync(cancellationToken);
        if (attachments.Count == 0)
        {
            return AttachmentRemoval.Empty;
        }

        if (!await StorageIsHealthyAsync(cancellationToken))
        {
            return AttachmentRemoval.Unavailable;
        }

        context.Attachments.RemoveRange(attachments);

        return new AttachmentRemoval(
            true,
            attachments.Select(attachment => attachment.StorageKey).ToArray());
    }

    public async Task<AttachmentObjectDeletion> DeleteObjectsAsync(
        IReadOnlyCollection<string> storageKeys,
        CancellationToken cancellationToken)
    {
        var orphaned = new List<string>();
        foreach (var key in storageKeys)
        {
            if (!await DeleteObjectAsync(key, cancellationToken))
            {
                orphaned.Add(key);
            }
        }

        return new AttachmentObjectDeletion(storageKeys.Count - orphaned.Count, orphaned);
    }

    private Task<Attachment?> FindOwnedAsync(
        Guid userId,
        Guid attachmentId,
        CancellationToken cancellationToken) => context.Attachments
        .SingleOrDefaultAsync(attachment => attachment.PublicId == attachmentId &&
            attachment.Transaction.User.PublicId == userId,
            cancellationToken);

    private async Task<bool> StorageIsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await storage.IsHealthyAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> DeleteObjectAsync(
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(key, cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
}
