using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Currencies;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class ExchangeRateStoreTests
{
    private static readonly DateOnly RateDate = new(2026, 9, 10);

    [FunctionalFact]
    public async Task GivenRateBeyondStorageScale_WhenSynchronizedTwice_ThenSecondRunLeavesItUnchanged()
    {
        await WithCurrenciesAsync(async context =>
        {
            var store = new EfExchangeRateStore(context);

            var first = await store.UpsertPublishedAsync(
                [new PublishedRateCandidate("USD", "BRL", 5.123456789m, RateDate)],
                CancellationToken.None);
            var second = await store.UpsertPublishedAsync(
                [new PublishedRateCandidate("USD", "BRL", 5.1234567912m, RateDate)],
                CancellationToken.None);

            Assert.Equal(new PublishedRateUpsertResult(1, 0), first);
            Assert.Equal(new PublishedRateUpsertResult(0, 1), second);
            context.ChangeTracker.Clear();
            Assert.Equal(5.12345679m, (await context.ExchangeRates.SingleAsync()).Rate);
        });
    }

    [FunctionalFact]
    public async Task GivenManualRateBeyondStorageScale_WhenRecorded_ThenStoredRoundedRateIsReturned()
    {
        await WithCurrenciesAsync(async context =>
        {
            var result = await new EfExchangeRateStore(context).UpsertManualAsync(
                new ManualRateCandidate("USD", "BRL", 5.000000005m, RateDate),
                CancellationToken.None);

            Assert.Equal(ManualRateUpsertOutcome.Succeeded, result.Outcome);
            Assert.Equal(5.00000001m, result.Rate);
        });
    }

    [FunctionalFact]
    public async Task GivenUnknownCurrency_WhenRatesAreUpserted_ThenOutcomesReportItWithoutThrowing()
    {
        await WithCurrenciesAsync(async context =>
        {
            var store = new EfExchangeRateStore(context);

            var published = await store.UpsertPublishedAsync(
                [
                    new PublishedRateCandidate("USD", "BRL", 5m, RateDate),
                    new PublishedRateCandidate("XTS", "BRL", 2m, RateDate)
                ],
                CancellationToken.None);
            var manual = await store.UpsertManualAsync(
                new ManualRateCandidate("XTS", "BRL", 2m, RateDate),
                CancellationToken.None);

            Assert.Equal(1, published.StoredCount);
            Assert.Equal(1, published.SkippedCount);
            Assert.Equal(["XTS"], published.MissingCurrencyCodes);
            Assert.Equal(ManualRateUpsertOutcome.CurrencyNotSupported, manual.Outcome);
        });
    }

    private static async Task WithCurrenciesAsync(Func<AppDbContext, Task> test)
    {
        var path = SqliteTestDatabase.TemporaryPath("fortuna-rates");
        try
        {
            await using var context = SqliteTestDatabase.CreateContext(path);
            await context.Database.MigrateAsync();
            context.Currencies.AddRange(
                new Currency("USD", "US Dollar", 2),
                new Currency("BRL", "Brazilian Real", 2));
            await context.SaveChangesAsync();
            await test(context);
        }
        finally
        {
            SqliteTestDatabase.Delete(path);
        }
    }
}
