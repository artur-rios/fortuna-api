using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Investments;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class InvestmentPositionReaderTests
{
    [FunctionalFact]
    public async Task GivenMovementsOnTheValuationDay_WhenReadingPositions_ThenTheCalculatorRuleApplies()
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var investment = new Investment(
                data.User, "Bond", null, InvestmentType.FixedIncome, data.Currency,
                StoreTestData.Now.AddDays(-10));
            var valuedOn = StoreTestData.Today;
            var valuedAt = StoreTestData.Now;
            var before = new InvestmentMovement(
                investment, InvestmentMovementType.Contribution, 300m, valuedOn,
                valuedAt.AddHours(-1));
            var valuation = new InvestmentValuation(investment, 1000m, valuedOn, valuedAt);
            var after = new InvestmentMovement(
                investment, InvestmentMovementType.Contribution, 200m, valuedOn,
                valuedAt.AddHours(1));
            context.AddRange(investment, before, valuation, after);
            await context.SaveChangesAsync();
            var movements = await context.InvestmentMovements.ToListAsync();
            var valuations = await context.InvestmentValuations.ToListAsync();

            var positions = await InvestmentPositionReader.CalculateAsync(
                context, [investment.Id], valuedOn, CancellationToken.None);

            // The movement recorded before the valuation is part of it; the one after is not.
            var expected = InvestmentPositionCalculator.Calculate(movements, valuations).Value;
            Assert.Equal(1200m, expected);
            Assert.Equal(expected, positions[investment.Id]);
        });
    }

    [FunctionalFact]
    public async Task GivenValuedInvestment_WhenMovementIsRecorded_ThenReturnedPositionStartsFromTheValuation()
    {
        await WithDatabaseAsync(async context =>
        {
            var data = await StoreTestData.SeedAsync(context);
            var investment = new Investment(
                data.User, "Fund", null, InvestmentType.Fund, data.Currency,
                StoreTestData.Now.AddDays(-10));
            var contribution = new InvestmentMovement(
                investment, InvestmentMovementType.Contribution, 3000m,
                StoreTestData.Today.AddDays(-5), StoreTestData.Now.AddDays(-5));
            var valuation = new InvestmentValuation(
                investment, 5000m, StoreTestData.Today.AddDays(-2), StoreTestData.Now.AddDays(-2));
            context.AddRange(investment, contribution, valuation);
            await context.SaveChangesAsync();

            var result = await new EfInvestmentMovementStore(context).RecordAsync(
                new InvestmentMovementRecord(
                    data.User.PublicId,
                    investment.PublicId,
                    InvestmentMovementType.Contribution,
                    100m,
                    StoreTestData.Today,
                    null,
                    StoreTestData.Now),
                CancellationToken.None);

            Assert.Equal(InvestmentMovementRecordOutcome.Succeeded, result.Outcome);
            Assert.Equal(5100m, result.Movement!.Position);
        });
    }

    private static async Task WithDatabaseAsync(Func<AppDbContext, Task> test)
    {
        var path = SqliteTestDatabase.TemporaryPath("fortuna-investment-positions");
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
