using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class ExchangeRateTests
{
    [UnitFact]
    public void GivenValidRate_WhenCreated_ThenExactDecimalAndMetadataArePreserved()
    {
        var date = new DateOnly(2026, 9, 3);

        var rate = new ExchangeRate(1, 2, 5.43219876m, date, ExchangeRateSource.Published);

        Assert.Equal(1, rate.BaseCurrencyId);
        Assert.Equal(2, rate.QuoteCurrencyId);
        Assert.Equal(5.43219876m, rate.Rate);
        Assert.Equal(date, rate.RateDate);
        Assert.Equal(ExchangeRateSource.Published, rate.Source);
    }

    [UnitFact]
    public void GivenSameBaseAndQuote_WhenCreated_ThenArgumentExceptionIsThrown()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ExchangeRate(1, 1, 1m, DateOnly.MinValue, ExchangeRateSource.Manual));

        Assert.Equal("quoteCurrencyId", exception.ParamName);
    }

    [UnitTheory]
    [InlineData("0")]
    [InlineData("-0.01")]
    public void GivenNonPositiveRate_WhenCreated_ThenArgumentOutOfRangeExceptionIsThrown(string value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExchangeRate(1, 2, decimal.Parse(value), DateOnly.MinValue, ExchangeRateSource.Manual));

        Assert.Equal("rate", exception.ParamName);
    }

    [UnitFact]
    public void GivenPublishedRate_WhenSourceValueChanges_ThenExactValueIsReplaced()
    {
        var rate = new ExchangeRate(1, 2, 5.1m, DateOnly.MinValue, ExchangeRateSource.Published);

        var changed = rate.ReplacePublishedRate(5.2m);
        var unchanged = rate.ReplacePublishedRate(5.2m);

        Assert.True(changed);
        Assert.False(unchanged);
        Assert.Equal(5.2m, rate.Rate);
    }

    [UnitFact]
    public void GivenManualRate_WhenSynchronizationTriesToReplaceIt_ThenOperationIsRejected()
    {
        var rate = new ExchangeRate(1, 2, 5.1m, DateOnly.MinValue, ExchangeRateSource.Manual);

        Assert.Throws<InvalidOperationException>(() => rate.ReplacePublishedRate(5.2m));
        Assert.Equal(5.1m, rate.Rate);
    }

    [UnitFact]
    public void GivenManualRate_WhenManualValueChanges_ThenExactValueIsReplaced()
    {
        var rate = new ExchangeRate(1, 2, 5.1m, DateOnly.MinValue, ExchangeRateSource.Manual);

        var changed = rate.ReplaceManualRate(5.2m);
        var unchanged = rate.ReplaceManualRate(5.2m);

        Assert.True(changed);
        Assert.False(unchanged);
        Assert.Equal(5.2m, rate.Rate);
    }

    [UnitFact]
    public void GivenPublishedRate_WhenManualReplacementIsAttempted_ThenOperationIsRejected()
    {
        var rate = new ExchangeRate(1, 2, 5.1m, DateOnly.MinValue, ExchangeRateSource.Published);

        Assert.Throws<InvalidOperationException>(() => rate.ReplaceManualRate(5.2m));
    }
}
