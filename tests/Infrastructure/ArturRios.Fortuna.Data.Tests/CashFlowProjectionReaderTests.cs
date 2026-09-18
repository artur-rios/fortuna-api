using ArturRios.Fortuna.Data.Projections;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Projections;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class CashFlowProjectionReaderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
    private static readonly DateOnly AsOf = new(2026, 9, 1);

    [FunctionalFact]
    public async Task GivenMaterializedRecurringOccurrence_WhenRead_ThenHistoryExcludesIt()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        Guid userId;
        await using (var context = database.CreateContext())
        {
            var currency = new Currency("BRL", "Brazilian Real", 2);
            var user = new UserProfile(Guid.NewGuid(), "Owner", currency, Now);
            context.AddRange(currency, user);
            await context.SaveChangesAsync();
            var account = new FinancialAccount(
                user, "Checking", null, FinancialAccountType.Checking, currency, 0m, Now);
            var category = new Category(user, "Rent", Now);
            context.AddRange(account, category);
            await context.SaveChangesAsync();
            var rule = new RecurringTransaction(
                user, account, null, category, TransactionDirection.Expense, 100m,
                RecurrenceFrequency.Monthly, AsOf.AddMonths(-1), null, Now);
            context.Add(rule);
            await context.SaveChangesAsync();
            var occurrence = new FinancialTransaction(
                user, account, category, TransactionDirection.Expense, 100m,
                AsOf.AddMonths(-1), Now, "Rent");
            occurrence.MarkAsRecurringOccurrence(rule, false, Now);
            var ordinary = new FinancialTransaction(
                user, account, category, TransactionDirection.Expense, 7m,
                AsOf.AddDays(-2), Now, "Coffee");
            context.AddRange(occurrence, ordinary);
            await context.SaveChangesAsync();
            userId = user.PublicId;
        }

        await using var reader = database.CreateContext();
        var snapshot = await new EfCashFlowProjectionReader(reader).ReadAsync(
            userId,
            AsOf,
            AsOf.AddDays(40),
            AsOf.AddDays(-89),
            CancellationToken.None);

        var history = Assert.Single(snapshot.HistoricalFigures);
        Assert.Equal(-7m, history.SignedAmount);
        Assert.Equal(CashFlowSourceKind.Historical, history.Source);
        Assert.Contains(snapshot.FutureFigures, figure =>
            figure.Source == CashFlowSourceKind.Recurring && figure.SignedAmount == -100m);
    }
}
