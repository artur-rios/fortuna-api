using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.Query.Conversion;

/// <summary>
/// Converts figures into one display currency for the lifetime of a single query.
/// </summary>
/// <remarks>
/// <para>
/// Rate date: the caller picks it. Historical figures (transactions, statement charges,
/// scheduled flows) pass their own date; point-in-time positions (balances, net position,
/// investment positions) pass the figure or as-of date.
/// </para>
/// <para>
/// Rounding: converted values are kept at full precision. Individual display values are
/// rounded to the display currency's minor unit with <see cref="Round(decimal)"/>, and
/// totals are summed at full precision and rounded once by <see cref="Total"/>.
/// </para>
/// <para>
/// Missing rates never fail a query: the figure stays unconverted, every total that
/// depends on it is <see langword="null"/>, and the currency pair is reported through
/// <see cref="MissingRates"/>.
/// </para>
/// </remarks>
public sealed class FigureConverter(IExchangeRateReader rates, CurrencySnapshot displayCurrency)
{
    private readonly Dictionary<(string CurrencyCode, DateOnly RateDate), ExchangeRateSnapshot?>
        _cache = [];
    private readonly List<ExchangeRateSnapshot> _applied = [];
    private readonly SortedSet<string> _missing = new(StringComparer.Ordinal);

    public CurrencySnapshot DisplayCurrency => displayCurrency;

    public bool IsFullyConverted => _missing.Count == 0;

    public IReadOnlyCollection<ExchangeRateSnapshot> AppliedRates => _applied;

    public IReadOnlyCollection<MissingExchangeRate> MissingRates => _missing
        .Select(code => new MissingExchangeRate(code, displayCurrency.Code))
        .ToArray();

    public async Task<FigureConversion> ConvertAsync(
        string sourceCurrencyCode,
        decimal amount,
        DateOnly rateDate)
    {
        if (IsDisplayCurrency(sourceCurrencyCode))
        {
            return new FigureConversion(sourceCurrencyCode, amount, rateDate, amount, null);
        }

        var rate = await FindRateAsync(sourceCurrencyCode, rateDate);

        return new FigureConversion(
            sourceCurrencyCode,
            amount,
            rateDate,
            rate is null ? null : amount * rate.Rate,
            rate);
    }

    public async Task<ExchangeRateSnapshot?> FindRateAsync(
        string sourceCurrencyCode,
        DateOnly rateDate)
    {
        if (IsDisplayCurrency(sourceCurrencyCode))
        {
            return null;
        }

        var key = (sourceCurrencyCode, rateDate);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var rate = await rates.FindApplicableAsync(
            sourceCurrencyCode,
            displayCurrency.Code,
            rateDate,
            CancellationToken.None);
        _cache[key] = rate;
        if (rate is null)
        {
            _missing.Add(sourceCurrencyCode);
        }
        else if (!_applied.Contains(rate))
        {
            _applied.Add(rate);
        }

        return rate;
    }

    public bool IsDisplayCurrency(string? currencyCode) =>
        string.Equals(currencyCode, displayCurrency.Code, StringComparison.Ordinal);

    public decimal Round(decimal value) =>
        decimal.Round(value, displayCurrency.MinorUnitDigits, MidpointRounding.AwayFromZero);

    public decimal? Round(decimal? value) => value.HasValue ? Round(value.Value) : null;

    public decimal? Total(IEnumerable<FigureConversion> conversions) =>
        Total(conversions.Select(conversion => conversion.Value));

    public decimal? Total(IEnumerable<decimal?> values)
    {
        var total = 0m;
        foreach (var value in values)
        {
            if (!value.HasValue)
            {
                return null;
            }

            total += value.Value;
        }

        return Round(total);
    }
}

public sealed record FigureConversion(
    string SourceCurrencyCode,
    decimal SourceAmount,
    DateOnly RateDate,
    decimal? Value,
    ExchangeRateSnapshot? Rate)
{
    public bool IsConverted => Value.HasValue;

    public string? UnconvertedReason => IsConverted
        ? null
        : FigureConversionMessages.RateUnavailable;
}

public sealed record MissingExchangeRate(string BaseCurrencyCode, string QuoteCurrencyCode);
