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

    Task<bool> IsOwnedTransactionAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken);

    IQueryable<AttachmentListSnapshot> QueryForTransaction(Guid userId, Guid transactionId);
}

public sealed class AttachmentListSnapshot
{
    public Guid Id { get; init; }
    public Guid TransactionId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeInBytes { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public interface IAttachmentLifecycleStore
{
    Task<AttachmentLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid attachmentId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<AttachmentLifecycleResult> HardDeleteAsync(
        Guid userId,
        Guid attachmentId,
        CancellationToken cancellationToken);

    Task SoftDeleteForTransactionsAsync(
        IReadOnlyDictionary<long, Guid> transactionCascadeIds,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task RestoreForTransactionsAsync(
        IReadOnlyDictionary<long, Guid> transactionCascadeIds,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the attachments of the given transactions for removal in the caller's unit of
    /// work without touching storage. The caller deletes the returned objects through
    /// <see cref="DeleteObjectsAsync"/> only after its database transaction commits.
    /// </summary>
    Task<AttachmentRemoval> RemoveForTransactionsAsync(
        IReadOnlyCollection<long> transactionIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes stored objects whose metadata was already removed and committed. Failures leave
    /// orphaned objects behind instead of failing the committed deletion.
    /// </summary>
    Task<AttachmentObjectDeletion> DeleteObjectsAsync(
        IReadOnlyCollection<string> storageKeys,
        CancellationToken cancellationToken);
}

public sealed record AttachmentRemoval(
    bool StorageAvailable,
    IReadOnlyCollection<string> StorageKeys)
{
    public static AttachmentRemoval Unavailable { get; } = new(false, []);
    public static AttachmentRemoval Empty { get; } = new(true, []);
}

public sealed record AttachmentObjectDeletion(
    int Deleted,
    IReadOnlyCollection<string> Orphaned);

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

public enum AttachmentLifecycleOutcome
{
    Succeeded = 1,
    NotFound = 2,
    HardDeleteRequiresSoftDeletion = 3,
    StorageUnavailable = 4
}

public sealed record AttachmentLifecycleResult(
    AttachmentLifecycleOutcome Outcome,
    Guid? Id = null,
    bool? IsDeleted = null);

public sealed record AttachmentOptions(
    int MaximumBytes,
    IReadOnlyCollection<string> AllowedContentTypes);
