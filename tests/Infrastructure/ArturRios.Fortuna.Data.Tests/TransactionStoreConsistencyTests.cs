using ArturRios.Fortuna.Data.Attachments;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Transactions;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class TransactionStoreConsistencyTests
{
    [FunctionalFact]
    public async Task GivenChargeWrittenOutsideTheStore_WhenAnotherChargeIsRecorded_ThenStatementTotalIsRecomputedFromTheDatabase()
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var card = Card(context, data);
            await context.SaveChangesAsync();
            var store = Store(context);
            var first = await store.RecordAsync(Charge(data, card, 10m), CancellationToken.None);
            Assert.Equal(TransactionRecordOutcome.Succeeded, first.Outcome);

            // A concurrent writer adds a charge to the same statement without this context
            // seeing it; an in-memory running total would miss it.
            var statement = await context.CreditCardStatements.SingleAsync();
            var concurrent = new FinancialTransaction(
                data.User, card, data.Category, TransactionDirection.Expense, 7m,
                StoreTestData.Today, StoreTestData.Now);
            concurrent.AssignToStatement(statement, false, StoreTestData.Now);
            context.FinancialTransactions.Add(concurrent);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var second = await store.RecordAsync(Charge(data, card, 5m), CancellationToken.None);

            Assert.Equal(TransactionRecordOutcome.Succeeded, second.Outcome);
            Assert.Equal(22m, second.Transaction!.StatementPurchaseTotal);
            context.ChangeTracker.Clear();
            Assert.Equal(22m, (await context.CreditCardStatements.SingleAsync()).PurchaseTotal);
        });
    }

    [FunctionalFact]
    public async Task GivenCounterpartyCreatedConcurrently_WhenTransactionIsRecorded_ThenTheExistingCounterpartyIsReused()
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var account = data.Account(context, "Checking");
            await context.SaveChangesAsync();
            var raced = false;
            context.SavingChanges += (_, _) =>
            {
                if (raced || !context.ChangeTracker.Entries<Counterparty>()
                        .Any(entry => entry.State == EntityState.Added))
                {
                    return;
                }

                raced = true;
                var publicId = Guid.NewGuid().ToString().ToUpperInvariant();
                context.Database.ExecuteSql(
                    $"INSERT INTO counterparty (public_id, user_id, name, normalized_name, is_deleted, created_at, updated_at) VALUES ({publicId}, {data.User.Id}, 'Acme', 'ACME', 0, '2026-09-10 12:00:00+00:00', '2026-09-10 12:00:00+00:00')");
            };

            var result = await Store(context).RecordAsync(
                new TransactionRecord(
                    data.User.PublicId,
                    account.PublicId,
                    null,
                    data.Category.PublicId,
                    TransactionDirection.Expense,
                    3m,
                    null,
                    StoreTestData.Today,
                    null,
                    " acme ",
                    [],
                    StoreTestData.Now),
                CancellationToken.None);

            Assert.True(raced);
            Assert.Equal(TransactionRecordOutcome.Succeeded, result.Outcome);
            context.ChangeTracker.Clear();
            var counterparty = Assert.Single(await context.Counterparties.ToListAsync());
            Assert.Equal(counterparty.PublicId, result.Transaction!.CounterpartyId);
        });
    }

    private static CreditCard Card(AppDbContext context, StoreTestData data)
    {
        var card = new CreditCard(
            data.User, "Card", "Issuer", data.Currency, 1000m, 20, 5, null, StoreTestData.Now);
        context.CreditCards.Add(card);

        return card;
    }

    private static TransactionRecord Charge(StoreTestData data, CreditCard card, decimal amount) => new(
        data.User.PublicId,
        null,
        card.PublicId,
        data.Category.PublicId,
        TransactionDirection.Expense,
        amount,
        null,
        StoreTestData.Today,
        null,
        null,
        [],
        StoreTestData.Now);

    private static EfTransactionStore Store(AppDbContext context) => new(
        context,
        new EfAttachmentLifecycleStore(context, new RecordingAttachmentStore()));

    private static async Task WithDatabaseAsync(Func<AppDbContext, Task> test)
    {
        var path = SqliteTestDatabase.TemporaryPath("fortuna-transactions");
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
