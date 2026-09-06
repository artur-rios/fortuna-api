using ArturRios.Fortuna.Domain.Planning;

namespace ArturRios.Fortuna.Shared.Planning;

public interface IBudgetStore
{
    Task<BudgetMutationResult> CreateAsync(
        BudgetCreation creation,
        CancellationToken cancellationToken);
}

public interface IBudgetReader
{
    Task<IReadOnlyCollection<BudgetSnapshot>> ListAsync(
        Guid userId,
        bool includeDeleted,
        DateOnly asOf,
        CancellationToken cancellationToken);

    Task<BudgetSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        DateOnly asOf,
        CancellationToken cancellationToken);
}

public interface IBudgetUpdater
{
    Task<BudgetMutationResult> UpdateAsync(
        BudgetUpdate update,
        CancellationToken cancellationToken);
}

public interface IBudgetLifecycleStore
{
    Task<BudgetMutationResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        DateOnly asOf,
        CancellationToken cancellationToken);
}

public enum BudgetMutationOutcome
{
    Succeeded = 1,
    NotFound = 2,
    CategoryNotFound = 3,
    CurrencyNotFound = 4
}

public sealed record BudgetCreation(
    Guid UserId,
    decimal Amount,
    string CurrencyCode,
    BudgetPeriodType PeriodType,
    DateOnly PeriodStart,
    IReadOnlyCollection<Guid> CategoryIds,
    bool IncludeDescendants,
    DateTimeOffset CreatedAt);

public sealed record BudgetUpdate(
    Guid UserId,
    Guid Id,
    decimal Amount,
    string CurrencyCode,
    BudgetPeriodType PeriodType,
    DateOnly PeriodStart,
    IReadOnlyCollection<Guid> CategoryIds,
    bool IncludeDescendants,
    DateTimeOffset UpdatedAt);

public sealed record BudgetMutationResult(
    BudgetSnapshot? Budget,
    BudgetMutationOutcome Outcome);

public sealed record BudgetSnapshot(
    Guid Id,
    decimal Amount,
    string CurrencyCode,
    BudgetPeriodType PeriodType,
    DateOnly PeriodStart,
    bool IncludeDescendants,
    IReadOnlyCollection<BudgetCategorySnapshot> Categories,
    BudgetConsumptionSnapshot CurrentPeriod,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record BudgetCategorySnapshot(Guid Id, string Name);

public sealed record BudgetConsumptionSnapshot(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal? Spent,
    decimal? Remaining,
    bool? IsExceeded,
    decimal? Overage,
    bool IsFullyConverted);
