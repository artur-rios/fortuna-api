using ArturRios.Fortuna.Data.Configuration;
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
        var accounts = await context.FinancialAccounts
            .AsNoTracking()
            .Where(item =>
                item.User.PublicId == userId &&
                !item.IsDeleted &&
                item.CreatedAt <= endOfDay)
            .Select(item => new CurrencyAmount(
                item.Currency.Code,
                item.OpeningBalance + (context.FinancialTransactions
                    .Where(transaction =>
                        transaction.FinancialAccountId == item.Id &&
                        !transaction.IsDeleted &&
                        transaction.OccurredOn <= asOf)
                    .Select(transaction => (decimal?)(transaction.Direction ==
                        TransactionDirection.Earning
                            ? transaction.Amount
                            : -transaction.Amount))
                    .Sum() ?? 0m)))
            .ToArrayAsync(cancellationToken);

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
        var result = new List<CurrencyAmount>(investments.Length);
        foreach (var investment in investments)
        {
            var valuation = await context.InvestmentValuations
                .AsNoTracking()
                .Where(item =>
                    item.InvestmentId == investment.Id &&
                    !item.IsDeleted &&
                    item.ValuedOn <= asOf)
                .OrderByDescending(item => item.ValuedOn)
                .Select(item => new { item.Value, item.ValuedOn })
                .FirstOrDefaultAsync(cancellationToken);
            var movement = await context.InvestmentMovements
                .AsNoTracking()
                .Where(item =>
                    item.InvestmentId == investment.Id &&
                    !item.IsDeleted &&
                    item.OccurredOn <= asOf &&
                    (valuation == null || item.OccurredOn > valuation.ValuedOn))
                .Select(item => (decimal?)(item.MovementType == InvestmentMovementType.Contribution ||
                    item.MovementType == InvestmentMovementType.Yield
                        ? item.Amount
                        : -item.Amount))
                .SumAsync(cancellationToken) ?? 0m;
            result.Add(new CurrencyAmount(
                investment.CurrencyCode,
                (valuation?.Value ?? 0m) + movement));
        }

        return result;
    }

    private sealed record CurrencyAmount(string CurrencyCode, decimal Amount);
}
