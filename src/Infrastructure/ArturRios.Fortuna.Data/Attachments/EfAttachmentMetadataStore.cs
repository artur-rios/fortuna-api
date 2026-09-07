using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Shared.Attachments;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Attachments;

public sealed class EfAttachmentMetadataStore(AppDbContext context) : IAttachmentMetadataStore
{
    public Task<bool> IsOwnedLiveTransactionAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken) => context.FinancialTransactions.AnyAsync(
            transaction => transaction.PublicId == transactionId &&
                transaction.User.PublicId == userId &&
                !transaction.IsDeleted,
            cancellationToken);

    public async Task<AttachmentMetadataResult> CreateAsync(
        AttachmentMetadataWrite write,
        CancellationToken cancellationToken)
    {
        var transaction = await context.FinancialTransactions
            .SingleOrDefaultAsync(item => item.PublicId == write.TransactionId &&
                item.User.PublicId == write.UserId &&
                !item.IsDeleted,
                cancellationToken);
        if (transaction is null)
        {
            return new AttachmentMetadataResult(AttachmentMetadataOutcome.TransactionNotFound);
        }

        var attachment = new Attachment(
            transaction,
            write.FileName,
            write.ContentType,
            write.SizeInBytes,
            write.StorageKey,
            write.CreatedAt);
        context.Attachments.Add(attachment);
        await context.SaveChangesAsync(cancellationToken);

        return new AttachmentMetadataResult(
            AttachmentMetadataOutcome.Succeeded,
            new AttachmentSnapshot(
                attachment.PublicId,
                transaction.PublicId,
                attachment.FileName,
                attachment.ContentType,
                attachment.SizeInBytes,
                attachment.CreatedAt));
    }
}
