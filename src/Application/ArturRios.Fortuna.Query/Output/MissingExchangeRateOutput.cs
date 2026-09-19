using ArturRios.Fortuna.Query.Conversion;

namespace ArturRios.Fortuna.Query.Output;

public sealed class MissingExchangeRateOutput
{
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public string QuoteCurrencyCode { get; set; } = string.Empty;

    internal static IReadOnlyCollection<MissingExchangeRateOutput> From(
        FigureConverter converter) => converter.MissingRates
        .Select(rate => new MissingExchangeRateOutput
        {
            BaseCurrencyCode = rate.BaseCurrencyCode,
            QuoteCurrencyCode = rate.QuoteCurrencyCode
        })
        .ToArray();
}
