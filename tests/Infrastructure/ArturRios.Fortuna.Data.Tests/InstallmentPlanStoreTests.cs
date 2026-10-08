using ArturRios.Fortuna.Data.Attachments;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Transactions;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class InstallmentPlanStoreTests
{
    [FunctionalTheory]
    [InlineData(28, 2026, 1, 30)]
    [InlineData(30, 2026, 3, 31)]
    [InlineData(10, 2026, 9, 10)]
    public async Task GivenPurchaseNearMonthEnd_WhenRecorded_ThenEachInstallmentLandsInTheNextCycle(
        short closingDay,
        int year,
        int month,
        int day)
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var card = new CreditCard(
                data.User, "Card", "Issuer", data.Currency, 5000m, closingDay, 5, null,
                StoreTestData.Now);
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();
            var purchasedOn = new DateOnly(year, month, day);

            var result = await Store(context).RecordAsync(
                Record(data, card, 300m, 3, purchasedOn),
                CancellationToken.None);

            Assert.Equal(InstallmentPlanRecordOutcome.Succeeded, result.Outcome);
            var installments = result.Plan!.Installments.OrderBy(item => item.Number).ToArray();
            var expected = BillingCycle.Containing(purchasedOn, closingDay, 5);
            foreach (var installment in installments)
            {
                Assert.InRange(installment.OccurredOn, expected.PeriodStart, expected.PeriodEnd);
                expected = expected.Next(closingDay, 5);
            }

            Assert.Equal(3, installments.Select(item => item.StatementId).Distinct().Count());
            Assert.Equal(purchasedOn, installments[0].OccurredOn);
        });
    }

    [FunctionalFact]
    public async Task GivenTotalFinerThanTheMinorUnit_WhenRecorded_ThenPlanTotalEqualsItsInstallments()
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var card = new CreditCard(
                data.User, "Card", "Issuer", data.Currency, 5000m, 20, 5, null, StoreTestData.Now);
            context.CreditCards.Add(card);
            await context.SaveChangesAsync();

            var result = await Store(context).RecordAsync(
                Record(data, card, 100.005m, 3, StoreTestData.Today),
                CancellationToken.None);

            Assert.Equal(InstallmentPlanRecordOutcome.Succeeded, result.Outcome);
            Assert.Equal(
                result.Plan!.Installments.Sum(item => item.Amount),
                result.Plan.TotalAmount);
            Assert.Equal(100.01m, result.Plan.TotalAmount);
        });
    }

    private static InstallmentPlanRecord Record(
        StoreTestData data,
        CreditCard card,
        decimal total,
        short count,
        DateOnly purchasedOn) => new(
        data.User.PublicId,
        card.PublicId,
        data.Category.PublicId,
        total,
        count,
        purchasedOn,
        null,
        null,
        StoreTestData.Now);

    private static EfInstallmentPlanStore Store(AppDbContext context) => new(
        context,
        new EfTransactionStore(
            context,
            new EfAttachmentLifecycleStore(context, new RecordingAttachmentStore())));

    private static async Task WithDatabaseAsync(Func<AppDbContext, Task> test)
    {
        var path = SqliteTestDatabase.TemporaryPath("fortuna-installments");
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
