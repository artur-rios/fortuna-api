using ArturRios.Fortuna.Data.Accounts;
using ArturRios.Fortuna.Data.Attachments;
using ArturRios.Fortuna.Data.Classification;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Investments;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class HardDeletionTests
{
    [FunctionalFact]
    public async Task GivenDeletedTransferToAnotherAccount_WhenAccountIsHardDeleted_ThenBothLegsAndTransferAreRemoved()
    {
        await WithDatabaseAsync(async (context, path) =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var source = data.Account(context, "Source");
            var destination = data.Account(context, "Destination");
            var transfer = data.Transfer(context, source, destination, 25m);
            await context.SaveChangesAsync();
            var cascade = transfer.SoftDelete(StoreTestData.Now).CascadeId;
            transfer.OutboundTransaction.SoftDeleteFromCascade(cascade, StoreTestData.Now);
            transfer.InboundTransaction!.SoftDeleteFromCascade(cascade, StoreTestData.Now);
            source.SoftDelete(StoreTestData.Now);
            await context.SaveChangesAsync();
            var store = AccountStore(context, new RecordingAttachmentStore());

            var result = await store.HardDeleteAsync(
                data.User.PublicId,
                source.PublicId,
                CancellationToken.None);

            Assert.Equal(FinancialAccountLifecycleOutcome.Succeeded, result.Outcome);
            await using var assertion = SqliteTestDatabase.CreateContext(path);
            Assert.False(await assertion.Transfers.AnyAsync());
            Assert.False(await assertion.FinancialTransactions.AnyAsync());
            Assert.True(await assertion.FinancialAccounts.AnyAsync(
                item => item.PublicId == destination.PublicId));
        });
    }

    [FunctionalFact]
    public async Task GivenLiveTransferLegInAnotherAccount_WhenAccountIsHardDeleted_ThenDependentsAreReported()
    {
        await WithDatabaseAsync(async (context, path) =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var source = data.Account(context, "Source");
            var destination = data.Account(context, "Destination");
            var transfer = data.Transfer(context, source, destination, 25m);
            await context.SaveChangesAsync();
            transfer.OutboundTransaction.SoftDelete(StoreTestData.Now);
            source.SoftDelete(StoreTestData.Now);
            await context.SaveChangesAsync();
            var store = AccountStore(context, new RecordingAttachmentStore());

            var result = await store.HardDeleteAsync(
                data.User.PublicId,
                source.PublicId,
                CancellationToken.None);

            Assert.Equal(FinancialAccountLifecycleOutcome.HardDeleteHasDependents, result.Outcome);
            await using var assertion = SqliteTestDatabase.CreateContext(path);
            Assert.Equal(2, await assertion.FinancialTransactions.CountAsync());
            Assert.True(await assertion.Transfers.AnyAsync());
        });
    }

    [FunctionalFact]
    public async Task GivenRecurringRuleOnAccount_WhenAccountIsHardDeleted_ThenDependentsAreReported()
    {
        await WithDatabaseAsync(async (context, _) =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var account = data.Account(context, "Checking");
            context.RecurringTransactions.Add(new RecurringTransaction(
                data.User,
                account,
                null,
                data.Category,
                TransactionDirection.Expense,
                10m,
                RecurrenceFrequency.Monthly,
                StoreTestData.Today,
                null,
                StoreTestData.Now));
            await context.SaveChangesAsync();
            account.SoftDelete(StoreTestData.Now);
            await context.SaveChangesAsync();

            var result = await AccountStore(context, new RecordingAttachmentStore()).HardDeleteAsync(
                data.User.PublicId,
                account.PublicId,
                CancellationToken.None);

            Assert.Equal(FinancialAccountLifecycleOutcome.HardDeleteHasDependents, result.Outcome);
        });
    }

    [FunctionalFact]
    public async Task GivenAttachmentOnDeletedAccount_WhenHardDeleted_ThenObjectIsDeletedOnlyAfterCommit()
    {
        await WithDatabaseAsync(async (context, path) =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var account = data.Account(context, "Checking");
            var transaction = data.Transaction(context, account, TransactionDirection.Expense, 5m);
            context.Attachments.Add(new Attachment(
                transaction, "receipt.pdf", "application/pdf", 3, "attachments/receipt", StoreTestData.Now));
            await context.SaveChangesAsync();
            var cascade = account.SoftDelete(StoreTestData.Now).CascadeId;
            transaction.SoftDeleteFromCascade(cascade, StoreTestData.Now);
            await context.SaveChangesAsync();
            await using var observer = SqliteTestDatabase.CreateContext(path);
            var objects = new RecordingAttachmentStore(observer);
            objects.Put("attachments/receipt");

            var result = await AccountStore(context, objects).HardDeleteAsync(
                data.User.PublicId,
                account.PublicId,
                CancellationToken.None);

            Assert.Equal(FinancialAccountLifecycleOutcome.Succeeded, result.Outcome);
            Assert.Equal(["attachments/receipt"], objects.Deleted);
            Assert.Equal([false], objects.MetadataPresentAtDelete);
            Assert.Empty(objects.Keys);
        });
    }

    [FunctionalFact]
    public async Task GivenObjectDeletionFails_WhenAccountIsHardDeleted_ThenDatabaseDeletionStillSucceeds()
    {
        await WithDatabaseAsync(async (context, path) =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var account = data.Account(context, "Checking");
            var transaction = data.Transaction(context, account, TransactionDirection.Expense, 5m);
            context.Attachments.Add(new Attachment(
                transaction, "receipt.pdf", "application/pdf", 3, "attachments/receipt", StoreTestData.Now));
            await context.SaveChangesAsync();
            var cascade = account.SoftDelete(StoreTestData.Now).CascadeId;
            transaction.SoftDeleteFromCascade(cascade, StoreTestData.Now);
            await context.SaveChangesAsync();
            var objects = new RecordingAttachmentStore { FailDeletes = true };
            objects.Put("attachments/receipt");

            var result = await AccountStore(context, objects).HardDeleteAsync(
                data.User.PublicId,
                account.PublicId,
                CancellationToken.None);

            Assert.Equal(FinancialAccountLifecycleOutcome.Succeeded, result.Outcome);
            await using var assertion = SqliteTestDatabase.CreateContext(path);
            Assert.False(await assertion.Attachments.AnyAsync());
            Assert.False(await assertion.FinancialAccounts.AnyAsync());
            Assert.Equal(["attachments/receipt"], objects.Keys);
        });
    }

    [FunctionalFact]
    public async Task GivenLiveBudgetOnCategory_WhenCategoryIsHardDeleted_ThenDependentsAreReported()
    {
        await WithDatabaseAsync(async (context, _) =>
        {
            var data = await StoreTestData.SeedAsync(context);
            context.Budgets.Add(new Budget(
                data.User,
                100m,
                data.Currency,
                BudgetPeriodType.Monthly,
                new DateOnly(2026, 9, 1),
                [data.Category],
                false,
                StoreTestData.Now));
            await context.SaveChangesAsync();
            data.Category.SoftDelete(StoreTestData.Now);
            await context.SaveChangesAsync();
            var store = new EfCategoryStore(
                context,
                new EfAttachmentLifecycleStore(context, new RecordingAttachmentStore()));

            var result = await store.HardDeleteAsync(
                data.User.PublicId,
                data.Category.PublicId,
                CancellationToken.None);

            Assert.Equal(CategoryLifecycleOutcome.HardDeleteHasDependents, result.Outcome);
        });
    }

    [FunctionalFact]
    public async Task GivenLiveFundingTransfer_WhenInvestmentIsHardDeleted_ThenDependentsAreReported()
    {
        await WithDatabaseAsync(async (context, path) =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var account = data.Account(context, "Checking");
            var investment = new Investment(
                data.User, "Treasury", null, InvestmentType.FixedIncome, data.Currency, StoreTestData.Now);
            var movement = new InvestmentMovement(
                investment, InvestmentMovementType.Contribution, 50m, StoreTestData.Today, StoreTestData.Now);
            context.AddRange(investment, movement);
            context.Transfers.Add(new Transfer(
                data.Transaction(context, account, TransactionDirection.Expense, 50m),
                movement,
                null,
                null,
                StoreTestData.Now));
            await context.SaveChangesAsync();
            var cascade = investment.SoftDelete(StoreTestData.Now).CascadeId;
            movement.SoftDeleteFromCascade(cascade, StoreTestData.Now);
            await context.SaveChangesAsync();
            var store = new EfInvestmentStore(
                context,
                new EfAttachmentLifecycleStore(context, new RecordingAttachmentStore()));

            var result = await store.HardDeleteAsync(
                data.User.PublicId,
                investment.PublicId,
                CancellationToken.None);

            Assert.Equal(InvestmentLifecycleOutcome.HardDeleteHasDependents, result.Outcome);
            await using var assertion = SqliteTestDatabase.CreateContext(path);
            Assert.True(await assertion.Transfers.AnyAsync());
            Assert.True(await assertion.FinancialTransactions.AnyAsync(item => !item.IsDeleted));
        });
    }

    private static EfFinancialAccountStore AccountStore(
        AppDbContext context,
        RecordingAttachmentStore objects) =>
        new(context, new EfAttachmentLifecycleStore(context, objects));

    private static async Task WithDatabaseAsync(Func<AppDbContext, string, Task> test)
    {
        var path = SqliteTestDatabase.TemporaryPath("fortuna-hard-delete");
        try
        {
            await using var context = SqliteTestDatabase.CreateContext(path);
            await context.Database.MigrateAsync();
            await test(context, path);
        }
        finally
        {
            SqliteTestDatabase.Delete(path);
        }
    }
}
