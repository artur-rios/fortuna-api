namespace ArturRios.Fortuna.Shared.Attachments;

public interface IAttachmentMetadataStore
{
    Task<bool> IsOwnedLiveTransactionAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken);

    Task<AttachmentMetadataResult> CreateAsync(
        AttachmentMetadataWrite write,
        CancellationToken cancellationToken);
}

public interface IAttachmentMetadataReader
{
    Task<AttachmentReadSnapshot?> FindOwnedAsync(
        Guid userId,
        Guid attachmentId,
        CancellationToken cancellationToken);
}

public sealed record AttachmentMetadataWrite(
    Guid UserId,
    Guid TransactionId,
    string FileName,
    string ContentType,
    long SizeInBytes,
    string StorageKey,
    DateTimeOffset CreatedAt);

public enum AttachmentMetadataOutcome
{
    Succeeded = 1,
    TransactionNotFound = 2
}

public sealed record AttachmentMetadataResult(
    AttachmentMetadataOutcome Outcome,
    AttachmentSnapshot? Attachment = null);

public sealed record AttachmentSnapshot(
    Guid Id,
    Guid TransactionId,
    string FileName,
    string ContentType,
    long SizeInBytes,
    DateTimeOffset CreatedAt);

public sealed record AttachmentReadSnapshot(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeInBytes,
    string StorageKey);

public sealed record AttachmentOptions(
    int MaximumBytes,
    IReadOnlyCollection<string> AllowedContentTypes);
