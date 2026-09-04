using ArturRios.Fortuna.Domain.Accounts;

namespace ArturRios.Fortuna.Shared.Accounts;

public interface IFinancialAccountStore
{
    Task<FinancialAccountCreationResult> CreateAsync(
        FinancialAccountCreation creation,
        CancellationToken cancellationToken);
}

public interface IFinancialAccountReader
{
    IQueryable<FinancialAccount> Query();

    Task<FinancialAccountSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken);

    Task<FinancialAccountBalanceSnapshot?> CalculateBalanceAsync(
        Guid userId,
        Guid id,
        DateOnly asOf,
        CancellationToken cancellationToken);
}

public interface IFinancialAccountUpdater
{
    Task<FinancialAccountUpdateResult> UpdateAsync(
        FinancialAccountUpdate update,
        CancellationToken cancellationToken);
}

public interface IFinancialAccountLifecycleStore
{
    Task<FinancialAccountLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<FinancialAccountLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<FinancialAccountLifecycleResult> HardDeleteAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);
}

public enum FinancialAccountLifecycleOutcome
{
    Succeeded = 1,
    NotFound = 2,
    RestoreRequiresSoftDeletion = 3,
    HardDeleteRequiresSoftDeletion = 4,
    HardDeleteHasLiveTransactions = 5,
    DuplicateName = 6
}

public sealed record FinancialAccountLifecycleResult(
    Guid? Id,
    FinancialAccountLifecycleOutcome Outcome);

public sealed record FinancialAccountCreation(
    Guid UserId,
    string Name,
    string? Institution,
    FinancialAccountType AccountType,
    string CurrencyCode,
    decimal OpeningBalance,
    DateTimeOffset CreatedAt);

public sealed record FinancialAccountCreationResult(
    FinancialAccountSnapshot? Account,
    bool DuplicateName);

public sealed record FinancialAccountUpdate(
    Guid UserId,
    Guid Id,
    string Name,
    string? Institution,
    FinancialAccountType AccountType,
    DateTimeOffset UpdatedAt);

public sealed record FinancialAccountUpdateResult(
    FinancialAccountSnapshot? Account,
    bool DuplicateName);

public sealed record FinancialAccountSnapshot(
    Guid Id,
    Guid UserId,
    string Name,
    string? Institution,
    FinancialAccountType AccountType,
    string CurrencyCode,
    decimal OpeningBalance,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record FinancialAccountBalanceSnapshot(
    Guid Id,
    string CurrencyCode,
    decimal Balance,
    DateOnly AsOf);
