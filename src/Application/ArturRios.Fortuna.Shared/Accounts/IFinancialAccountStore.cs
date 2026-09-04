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
}

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
