using ArturRios.Fortuna.Data.Accounts;
using ArturRios.Fortuna.Data.Attachments;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Planning;
using ArturRios.Fortuna.Data.Reporting;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class FinancialAccountStoreTests
{
    [FunctionalFact]
    public async Task GivenTransferAndAttachment_WhenAccountIsSoftDeletedAndRestored_ThenTheWholeTransferFollows()
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var source = data.Account(context, "Source");
            var destination = data.Account(context, "Destination");
            var transfer = data.Transfer(context, source, destination, 25m);
            var attachment = new Attachment(
                transfer.InboundTransaction!, "receipt.pdf", "application/pdf", 3, "attachments/r", StoreTestData.Now);
            context.Attachments.Add(attachment);
            await context.SaveChangesAsync();
            var store = Store(context);

            var deleted = await store.SoftDeleteAsync(
                data.User.PublicId, source.PublicId, StoreTestData.Now, CancellationToken.None);

            Assert.Equal(FinancialAccountLifecycleOutcome.Succeeded, deleted.Outcome);
            context.ChangeTracker.Clear();
            Assert.True((await context.Transfers.SingleAsync()).IsDeleted);
            Assert.All(await context.FinancialTransactions.ToListAsync(), item => Assert.True(item.IsDeleted));
            Assert.True((await context.Attachments.SingleAsync()).IsDeleted);
            Assert.False((await context.FinancialAccounts.SingleAsync(
                item => item.PublicId == destination.PublicId)).IsDeleted);

            var restored = await store.RestoreAsync(
                data.User.PublicId, source.PublicId, StoreTestData.Now.AddMinutes(1), CancellationToken.None);

            Assert.Equal(FinancialAccountLifecycleOutcome.Succeeded, restored.Outcome);
            context.ChangeTracker.Clear();
            Assert.False((await context.Transfers.SingleAsync()).IsDeleted);
            Assert.All(await context.FinancialTransactions.ToListAsync(), item => Assert.False(item.IsDeleted));
            Assert.False((await context.Attachments.SingleAsync()).IsDeleted);
        });
    }

    [FunctionalTheory]
    [InlineData("2026-08-05", 100)]
    [InlineData("2026-09-10", 160)]
    public async Task GivenMovementsDatedBeforeTheAccountOpened_WhenBalanceIsReadAnywhere_ThenEveryReaderAgrees(
        string asOfText,
        decimal expected)
    {
        await WithDatabaseAsync(async context =>
        {
            var asOf = DateOnly.Parse(asOfText);
            var data = await StoreTestData.SeedAsync(context);
            var account = data.Account(context, "Checking", openingBalance: 100m);
            data.Transaction(context, account, TransactionDirection.Earning, 50m, new DateOnly(2026, 8, 1));
            data.Transaction(context, account, TransactionDirection.Earning, 10m, new DateOnly(2026, 9, 1));
            var goal = new Goal(
                data.User,
                "Reserve",
                1000m,
                data.Currency,
                new DateOnly(2027, 1, 1),
                [account],
                [],
                StoreTestData.Now);
            context.Goals.Add(goal);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var balance = await Store(context).CalculateBalanceAsync(
                data.User.PublicId, account.PublicId, asOf, CancellationToken.None);
            var netPosition = await new EfNetPositionReader(context).ReadAsync(
                data.User.PublicId, asOf, CancellationToken.None);
            var progress = await new EfGoalStore(context).GetProgressAsync(
                data.User.PublicId, goal.PublicId, asOf, CancellationToken.None);

            Assert.Equal(expected, balance!.Balance);
            Assert.Equal(expected, Assert.Single(netPosition).FinancialAccounts);
            Assert.Equal(expected, Assert.Single(progress.Progress!.Resources).SourceAmount);
        });
    }

    private static EfFinancialAccountStore Store(AppDbContext context) => new(
        context,
        new EfAttachmentLifecycleStore(context, new RecordingAttachmentStore()));

    private static async Task WithDatabaseAsync(Func<AppDbContext, Task> test)
    {
        var path = SqliteTestDatabase.TemporaryPath("fortuna-accounts");
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
}
