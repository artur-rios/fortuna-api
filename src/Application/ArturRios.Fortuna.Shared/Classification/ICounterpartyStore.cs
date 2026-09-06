namespace ArturRios.Fortuna.Shared.Classification;

public interface ICounterpartyStore
{
    Task<CounterpartyCreationResult> CreateAsync(
        CounterpartyCreation creation,
        CancellationToken cancellationToken);
}

public interface ICounterpartyReader
{
    Task<IReadOnlyCollection<CounterpartySnapshot>> ListAsync(
        Guid userId,
        bool includeDeleted,
        CancellationToken cancellationToken);
}

public interface ICounterpartyUpdater
{
    Task<CounterpartyMutationResult> UpdateAsync(
        CounterpartyUpdate update,
        CancellationToken cancellationToken);
}

public interface ICounterpartyLifecycleStore
{
    Task<CounterpartyMutationResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);
}

public interface ICounterpartyMerger
{
    Task<CounterpartyMergeResult> MergeAsync(
        CounterpartyMerge merge,
        CancellationToken cancellationToken);
}

public interface ICounterpartyCategorySuggester
{
    Task<CounterpartyCategorySuggestionResult> SuggestCategoryAsync(
        Guid userId,
        Guid counterpartyId,
        CancellationToken cancellationToken);
}

public enum CounterpartyMutationOutcome
{
    Succeeded = 1,
    NotFound = 2,
    DuplicateName = 3
}

public sealed record CounterpartyCreation(
    Guid UserId,
    string Name,
    DateTimeOffset CreatedAt);

public sealed record CounterpartyCreationResult(
    CounterpartySnapshot? Counterparty,
    bool Reused,
    CounterpartyMutationOutcome Outcome);

public sealed record CounterpartyUpdate(
    Guid UserId,
    Guid Id,
    string Name,
    DateTimeOffset UpdatedAt);

public sealed record CounterpartyMutationResult(
    CounterpartySnapshot? Counterparty,
    CounterpartyMutationOutcome Outcome);

public enum CounterpartyMergeOutcome
{
    Succeeded = 1,
    NotFound = 2,
    SameCounterparty = 3
}

public sealed record CounterpartyMerge(
    Guid UserId,
    Guid SourceId,
    Guid TargetId,
    DateTimeOffset ChangedAt);

public sealed record CounterpartyMergeResult(
    Guid? SourceId,
    Guid? TargetId,
    int ReassignedTransactionCount,
    CounterpartyMergeOutcome Outcome);

public enum CounterpartyCategorySuggestionOutcome
{
    Succeeded = 1,
    NotFound = 2
}

public sealed record CounterpartyCategorySuggestionResult(
    Guid? CounterpartyId,
    Guid? CategoryId,
    string? CategoryName,
    CounterpartyCategorySuggestionOutcome Outcome);

public sealed record CounterpartySnapshot(
    Guid Id,
    string Name,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
