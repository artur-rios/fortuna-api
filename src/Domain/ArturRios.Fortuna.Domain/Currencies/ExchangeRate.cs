namespace ArturRios.Fortuna.Domain.Currencies;

public enum ExchangeRateSource : short
{
    Published = 1,
    Manual = 2
}

public sealed class ExchangeRate
{
    private ExchangeRate()
    {
    }

    public ExchangeRate(long baseCurrencyId, long quoteCurrencyId, decimal rate, DateOnly rateDate, ExchangeRateSource source)
    {
        if (baseCurrencyId == quoteCurrencyId)
        {
            throw new ArgumentException("Base and quote currencies must differ.", nameof(quoteCurrencyId));
        }

        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "An exchange rate must be positive.");
        }

        BaseCurrencyId = baseCurrencyId;
        QuoteCurrencyId = quoteCurrencyId;
        Rate = rate;
        RateDate = rateDate;
        Source = source;
    }

    public long Id { get; private set; }
    public long BaseCurrencyId { get; private set; }
    public long QuoteCurrencyId { get; private set; }
    public decimal Rate { get; private set; }
    public DateOnly RateDate { get; private set; }
    public ExchangeRateSource Source { get; private set; }
    public Currency BaseCurrency { get; private set; } = null!;
    public Currency QuoteCurrency { get; private set; } = null!;

    public bool ReplacePublishedRate(decimal rate)
    {
        if (Source != ExchangeRateSource.Published)
        {
            throw new InvalidOperationException("Only a published exchange rate can be replaced by synchronization.");
        }

        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "An exchange rate must be positive.");
        }

        if (Rate == rate)
        {
            return false;
        }

        Rate = rate;
        return true;
    }

    public bool ReplaceManualRate(decimal rate)
    {
        if (Source != ExchangeRateSource.Manual)
        {
            throw new InvalidOperationException("Only a manual exchange rate can be replaced manually.");
        }

        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "An exchange rate must be positive.");
        }

        if (Rate == rate)
        {
            return false;
        }

        Rate = rate;
        return true;
    }
}
