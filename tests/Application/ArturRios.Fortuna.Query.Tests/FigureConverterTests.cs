using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Query.Conversion;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class FigureConverterTests
{
    private static readonly DateOnly FigureDate = new(2026, 9, 4);
    private static readonly CurrencySnapshot Real = new("BRL", "Brazilian Real", 2);

    [UnitFact]
    public async Task GivenRepeatedPairAndDate_WhenConverted_ThenTheRateIsLookedUpOnce()
    {
        var rates = new StubRateReader(new ExchangeRateSnapshot(
            "USD", "BRL", 5m, FigureDate, ExchangeRateSource.Published));
        var converter = new FigureConverter(rates, Real);

        await converter.ConvertAsync("USD", 1m, FigureDate);
        await converter.ConvertAsync("USD", 2m, FigureDate);
        await converter.ConvertAsync("USD", 3m, FigureDate.AddDays(1));

        Assert.Equal(2, rates.CallCount);
        Assert.Single(converter.AppliedRates);
        Assert.True(converter.IsFullyConverted);
    }

    [UnitFact]
    public async Task GivenDisplayCurrencyFigure_WhenConverted_ThenNoRateIsRequested()
    {
        var rates = new StubRateReader(null);
        var converter = new FigureConverter(rates, Real);

        var conversion = await converter.ConvertAsync("BRL", 1.005m, FigureDate);

        Assert.Equal(1.005m, conversion.Value);
        Assert.Null(conversion.Rate);
        Assert.Null(conversion.UnconvertedReason);
        Assert.Equal(0, rates.CallCount);
    }

    [UnitFact]
    public async Task GivenSeveralFigures_WhenTotalled_ThenTheyAreSummedBeforeOneRounding()
    {
        var converter = new FigureConverter(new StubRateReader(new ExchangeRateSnapshot(
            "USD", "BRL", 1.5m, FigureDate, ExchangeRateSource.Manual)), Real);
        var first = await converter.ConvertAsync("USD", 0.335m, FigureDate);
        var second = await converter.ConvertAsync("USD", 0.335m, FigureDate);

        Assert.Equal(0.50m, converter.Round(first.Value));
        Assert.Equal(1.01m, converter.Total([first, second]));
    }

    [UnitFact]
    public async Task GivenMissingRate_WhenConverted_ThenTheFigureStaysUnconvertedAndThePairIsReported()
    {
        var converter = new FigureConverter(new StubRateReader(null), Real);
        var converted = await converter.ConvertAsync("BRL", 2m, FigureDate);
        var missing = await converter.ConvertAsync("USD", 3m, FigureDate);

        Assert.Null(missing.Value);
        Assert.Equal(FigureConversionMessages.RateUnavailable, missing.UnconvertedReason);
        Assert.Null(converter.Total([converted, missing]));
        Assert.False(converter.IsFullyConverted);
        var pair = Assert.Single(converter.MissingRates);
        Assert.Equal(new MissingExchangeRate("USD", "BRL"), pair);
    }

    [UnitFact]
    public void GivenZeroDigitCurrency_WhenRounded_ThenMidpointsRoundAwayFromZero()
    {
        var converter = new FigureConverter(
            new StubRateReader(null),
            new CurrencySnapshot("JPY", "Yen", 0));

        Assert.Equal(-3m, converter.Round(-2.5m));
        Assert.Equal(3m, converter.Total([1.2m, 1.3m]));
    }

    private sealed class StubRateReader(ExchangeRateSnapshot? rate) : IExchangeRateReader
    {
        public int CallCount { get; private set; }

        public Task<ExchangeRateSnapshot?> FindApplicableAsync(
            string baseCurrencyCode,
            string quoteCurrencyCode,
            DateOnly figureDate,
            CancellationToken cancellationToken)
        {
            CallCount++;

            return Task.FromResult(rate);
        }
    }
}
