using ArturRios.Fortuna.Data.Accounts;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Investments;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Reporting;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Reporting;

public sealed class EfNetPositionReader(AppDbContext context) : INetPositionReader
{
    public async Task<IReadOnlyCollection<NetPositionCurrencySnapshot>> ReadAsync(
        Guid userId,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var endOfDay = new DateTimeOffset(
            asOf.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
        var openings = await context.FinancialAccounts
            .AsNoTracking()
            .Where(item => item.User.PublicId == userId && !item.IsDeleted)
            .Select(item => new
            {
                Opening = new AccountOpening(item.Id, item.OpeningBalance, item.CreatedAt),
                CurrencyCode = item.Currency.Code
            })
            .ToArrayAsync(cancellationToken);
        var balances = await AccountBalanceCalculator.CalculateAsync(
            context,
            openings.Select(item => item.Opening).ToArray(),
            asOf,
            cancellationToken);
        var accounts = openings
            .Select(item => new CurrencyAmount(item.CurrencyCode, balances[item.Opening.Id]))
            .ToArray();

        var cards = await context.CreditCards
            .AsNoTracking()
            .Where(item =>
                item.User.PublicId == userId &&
                !item.IsDeleted &&
                item.CreatedAt <= endOfDay)
            .Select(item => new CurrencyAmount(
                item.Currency.Code,
                context.FinancialTransactions
                    .Where(transaction =>
                        transaction.CreditCardId == item.Id &&
                        !transaction.IsDeleted &&
                        transaction.OccurredOn <= asOf)
                    .Select(transaction => (decimal?)(transaction.Direction ==
                        TransactionDirection.Expense
                            ? transaction.Amount
                            : -transaction.Amount))
                    .Sum() ?? 0m))
            .ToArrayAsync(cancellationToken);

        var investments = await ReadInvestmentsAsync(userId, asOf, endOfDay, cancellationToken);

        return accounts.Select(item => (item.CurrencyCode, Accounts: item.Amount,
                Investments: 0m, Cards: 0m))
            .Concat(investments.Select(item => (item.CurrencyCode, Accounts: 0m,
                Investments: item.Amount, Cards: 0m)))
            .Concat(cards.Select(item => (item.CurrencyCode, Accounts: 0m,
                Investments: 0m, Cards: item.Amount)))
            .GroupBy(item => item.CurrencyCode, StringComparer.Ordinal)
            .Select(group => new NetPositionCurrencySnapshot(
                group.Key,
                group.Sum(item => item.Accounts),
                group.Sum(item => item.Investments),
                group.Sum(item => item.Cards)))
            .OrderBy(item => item.CurrencyCode, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyCollection<CurrencyAmount>> ReadInvestmentsAsync(
        Guid userId,
        DateOnly asOf,
        DateTimeOffset endOfDay,
        CancellationToken cancellationToken)
    {
        var investments = await context.Investments
            .AsNoTracking()
            .Where(item =>
                item.User.PublicId == userId &&
                !item.IsDeleted &&
                item.CreatedAt <= endOfDay)
            .Select(item => new { item.Id, CurrencyCode = item.Currency.Code })
            .ToArrayAsync(cancellationToken);
        var positions = await InvestmentPositionReader.CalculateAsync(
            context,
            investments.Select(item => item.Id).ToArray(),
            asOf,
            cancellationToken);

        return investments
            .Select(item => new CurrencyAmount(item.CurrencyCode, positions[item.Id]))
            .ToArray();
    }

    private sealed record CurrencyAmount(string CurrencyCode, decimal Amount);
}
