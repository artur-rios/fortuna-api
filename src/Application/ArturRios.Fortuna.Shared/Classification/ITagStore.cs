namespace ArturRios.Fortuna.Shared.Classification;

public interface ITagStore
{
    Task<TagCreationResult> CreateAsync(
        TagCreation creation,
        CancellationToken cancellationToken);
}

public interface ITagReader
{
    Task<IReadOnlyCollection<TagSnapshot>> ListAsync(
        Guid userId,
        bool includeDeleted,
        CancellationToken cancellationToken);
}

public interface ITagUpdater
{
    Task<TagUpdateResult> UpdateAsync(
        TagUpdate update,
        CancellationToken cancellationToken);
}

public interface ITagLifecycleStore
{
    Task<TagDeletionResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);
}

public interface ITransactionTagStore
{
    Task<TransactionTagAssignmentResult> AttachAsync(
        TransactionTagAssignment assignment,
        CancellationToken cancellationToken);

    Task<TransactionTagAssignmentResult> DetachAsync(
        TransactionTagAssignment assignment,
        CancellationToken cancellationToken);
}

public sealed record TagOptions(int MaximumPerTransaction);

public enum TagMutationOutcome
{
    Succeeded = 1,
    NotFound = 2,
    DuplicateName = 3
}

public sealed record TagCreation(
    Guid UserId,
    string Name,
    DateTimeOffset CreatedAt);

public sealed record TagCreationResult(
    TagSnapshot? Tag,
    TagMutationOutcome Outcome);

public sealed record TagUpdate(
    Guid UserId,
    Guid Id,
    string Name,
    DateTimeOffset UpdatedAt);

public sealed record TagUpdateResult(
    TagSnapshot? Tag,
    TagMutationOutcome Outcome);

public sealed record TagDeletionResult(
    TagSnapshot? Tag,
    int DetachedTransactionCount,
    TagMutationOutcome Outcome);

public enum TransactionTagAssignmentOutcome
{
    Succeeded = 1,
    NotFound = 2,
    MaximumExceeded = 3
}

public sealed record TransactionTagAssignment(
    Guid UserId,
    Guid TransactionId,
    Guid TagId,
    DateTimeOffset ChangedAt);

public sealed record TransactionTagAssignmentResult(
    Guid? TransactionId,
    Guid? TagId,
    bool IsAttached,
    bool Changed,
    int TagCount,
    TransactionTagAssignmentOutcome Outcome);

public sealed record TagSnapshot(
    Guid Id,
    string Name,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
