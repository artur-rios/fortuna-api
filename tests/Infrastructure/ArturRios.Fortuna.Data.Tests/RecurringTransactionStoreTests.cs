using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Transactions;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class RecurringTransactionStoreTests
{
    [FunctionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenSqliteRules_WhenListedByAmount_ThenTheyAreOrderedByExactAmount(bool descending)
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var account = data.Account(context, "Checking");
            foreach (var amount in new[] { 100.5m, 9.99m, 100.49m, 20m })
            {
                context.RecurringTransactions.Add(Rule(data, account, amount));
            }

            await context.SaveChangesAsync();

            var page = await Store(context).ListAsync(
                new RecurringTransactionListCriteria(
                    data.User.PublicId,
                    null,
                    false,
                    "Amount",
                    descending,
                    1,
                    3),
                CancellationToken.None);

            Assert.Equal(4, page.TotalItems);
            Assert.Equal(
                descending ? [100.5m, 100.49m, 20m] : [9.99m, 20m, 100.49m],
                page.Rules.Select(rule => rule.Amount));
        });
    }

    [FunctionalFact]
    public async Task GivenRuleDeletedBeforeItsTurn_WhenMaterialized_ThenItIsSkippedWithoutFailingTheRun()
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var account = data.Account(context, "Checking");
            var rule = Rule(data, account, 10m);
            context.RecurringTransactions.Add(rule);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            context.ChangeTracker.Tracked += new DeleteOnFirstRuleRead(context, rule.PublicId).OnTracked;

            var result = await Store(context).MaterializeAsync(
                new RecurringMaterializationRun(
                    data.User.PublicId,
                    StoreTestData.Today,
                    StoreTestData.Now),
                CancellationToken.None);

            var ruleResult = Assert.Single(result.Rules);
            Assert.Equal(RecurringMaterializationSkipReason.RuleDeleted, ruleResult.SkipReason);
            Assert.True(ruleResult.IsComplete);
            Assert.Empty(ruleResult.Occurrences);
        });
    }

    private static RecurringTransaction Rule(
        StoreTestData data,
        Domain.Accounts.FinancialAccount account,
        decimal amount) => new(
        data.User,
        account,
        null,
        data.Category,
        TransactionDirection.Expense,
        amount,
        RecurrenceFrequency.Monthly,
        StoreTestData.Today,
        null,
        StoreTestData.Now);

    private static EfRecurringTransactionStore Store(AppDbContext context) => new(
        context,
        TimeProvider.System,
        NullLogger<EfRecurringTransactionStore>.Instance);

    private static async Task WithDatabaseAsync(Func<AppDbContext, Task> test)
    {
        var path = SqliteTestDatabase.TemporaryPath("fortuna-recurring");
        try
        {
            await using var context = SqliteTestDatabase.CreateContext(path);
            await context.Database.MigrateAsync();
            await test(context);
        }
        finally
        {
            SqliteTestDatabase.Delete(path);
        }
    }

    // Soft-deletes the rule in the database as soon as the run first reads it, the way a user
    // deleting it while the materialization job runs would.
    private sealed class DeleteOnFirstRuleRead(AppDbContext context, Guid ruleId)
    {
        private bool deleted;

        public void OnTracked(
            object? sender,
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackedEventArgs args)
        {
            if (deleted || args.Entry.Entity is not RecurringTransaction rule || rule.PublicId != ruleId)
            {
                return;
            }

            deleted = true;
            var cascadeId = Guid.NewGuid().ToString().ToUpperInvariant();
            var publicId = ruleId.ToString().ToUpperInvariant();
            context.Database.ExecuteSql(
                $"UPDATE recurring_transaction SET is_deleted = 1, deletion_cascade_id = {cascadeId} WHERE public_id = {publicId}");
        }
    }
}
