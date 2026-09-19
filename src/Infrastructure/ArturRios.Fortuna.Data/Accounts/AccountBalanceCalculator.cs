using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Accounts;

internal sealed record AccountOpening(long Id, decimal OpeningBalance, DateTimeOffset CreatedAt);

// One balance rule for every reader (balance endpoint, net position, goals): before the UTC day
// the account was opened its balance is the opening balance; from that day on it is the opening
// balance plus every live movement dated on or before the requested day.
internal static class AccountBalanceCalculator
{
    public static async Task<IReadOnlyDictionary<long, decimal>> CalculateAsync(
        AppDbContext context,
        IReadOnlyCollection<AccountOpening> accounts,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var openIds = accounts
            .Where(account => !OpenedAfter(account, asOf))
            .Select(account => account.Id)
            .ToArray();
        var movements = openIds.Length == 0
            ? new Dictionary<long, decimal>()
            : await context.FinancialTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.FinancialAccountId.HasValue &&
                    openIds.Contains(transaction.FinancialAccountId.Value) &&
                    !transaction.IsDeleted &&
                    transaction.OccurredOn <= asOf)
                .GroupBy(transaction => transaction.FinancialAccountId!.Value)
                .Select(group => new
                {
                    AccountId = group.Key,
                    Movement = group.Sum(transaction =>
                        transaction.Direction == TransactionDirection.Earning
                            ? transaction.Amount
                            : -transaction.Amount)
                })
                .ToDictionaryAsync(item => item.AccountId, item => item.Movement, cancellationToken);

        return accounts.ToDictionary(
            account => account.Id,
            account => account.OpeningBalance + movements.GetValueOrDefault(account.Id));
    }

    private static bool OpenedAfter(AccountOpening account, DateOnly asOf) =>
        asOf < DateOnly.FromDateTime(account.CreatedAt.UtcDateTime);
}
