namespace ArturRios.Fortuna.Shared.Planning;

public interface IGoalStore
{
    Task<GoalMutationResult> CreateAsync(
        GoalCreation creation,
        CancellationToken cancellationToken);
}

public interface IGoalReader
{
    Task<IReadOnlyCollection<GoalSnapshot>> ListAsync(
        Guid userId,
        bool includeDeleted,
        DateOnly asOf,
        CancellationToken cancellationToken);

    Task<GoalSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        DateOnly asOf,
        CancellationToken cancellationToken);
}

public interface IGoalUpdater
{
    Task<GoalMutationResult> UpdateAsync(
        GoalUpdate update,
        CancellationToken cancellationToken);
}

public interface IGoalLifecycleStore
{
    Task<GoalMutationResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        DateOnly asOf,
        CancellationToken cancellationToken);
}

public enum GoalMutationOutcome
{
    Succeeded = 1,
    NotFound = 2,
    ResourceNotFound = 3,
    CurrencyNotFound = 4
}

public sealed record GoalCreation(
    Guid UserId,
    string Name,
    decimal TargetAmount,
    string CurrencyCode,
    DateOnly TargetDate,
    IReadOnlyCollection<Guid> AccountIds,
    IReadOnlyCollection<Guid> InvestmentIds,
    DateTimeOffset CreatedAt);

public sealed record GoalUpdate(
    Guid UserId,
    Guid Id,
    string Name,
    decimal TargetAmount,
    string CurrencyCode,
    DateOnly TargetDate,
    IReadOnlyCollection<Guid> AccountIds,
    IReadOnlyCollection<Guid> InvestmentIds,
    DateTimeOffset UpdatedAt);

public sealed record GoalMutationResult(GoalSnapshot? Goal, GoalMutationOutcome Outcome);

public sealed record GoalSnapshot(
    Guid Id,
    string Name,
    decimal TargetAmount,
    string CurrencyCode,
    DateOnly TargetDate,
    IReadOnlyCollection<GoalResourceSnapshot> Accounts,
    IReadOnlyCollection<GoalResourceSnapshot> Investments,
    GoalProgressSnapshot CurrentProgress,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GoalResourceSnapshot(Guid Id, string Name);

public sealed record GoalProgressSnapshot(
    decimal? CurrentAmount,
    decimal? Remaining,
    decimal? ProportionReached,
    bool? IsReached,
    bool IsFullyConverted);
