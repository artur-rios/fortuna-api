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

        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        context.Attachments.Remove(attachment);
        await context.SaveChangesAsync(cancellationToken);
        if (!await DeleteObjectAsync(attachment.StorageKey, cancellationToken))
        {
            await databaseTransaction.RollbackAsync(cancellationToken);
            context.Entry(attachment).State = EntityState.Unchanged;
            return new AttachmentLifecycleResult(AttachmentLifecycleOutcome.StorageUnavailable);
        }

        await databaseTransaction.CommitAsync(cancellationToken);
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

    public async Task<bool> HardDeleteForTransactionsAsync(
        IReadOnlyCollection<long> transactionIds,
        CancellationToken cancellationToken)
    {
        if (transactionIds.Count == 0)
        {
            return true;
        }

        var attachments = await context.Attachments
            .Where(attachment => transactionIds.Contains(attachment.TransactionId))
            .ToListAsync(cancellationToken);
        if (attachments.Count == 0)
        {
            return true;
        }

        if (!await StorageIsHealthyAsync(cancellationToken))
        {
            return false;
        }

        context.Attachments.RemoveRange(attachments);
        await context.SaveChangesAsync(cancellationToken);
        foreach (var attachment in attachments)
        {
            if (!await DeleteObjectAsync(attachment.StorageKey, cancellationToken))
            {
                foreach (var tracked in attachments)
                {
                    context.Entry(tracked).State = EntityState.Unchanged;
                }

                return false;
            }
        }

        return true;
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
        catch (Exception)
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
        catch (AttachmentObjectNotFoundException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
