using ArturRios.Fortuna.Domain.Currencies;

namespace ArturRios.Fortuna.Shared.Currencies;

public interface IExchangeRateReader
{
    Task<ExchangeRateSnapshot?> FindApplicableAsync(
        string baseCurrencyCode,
        string quoteCurrencyCode,
        DateOnly figureDate,
        CancellationToken cancellationToken);
}

public sealed record ExchangeRateSnapshot(
    string BaseCurrencyCode,
    string QuoteCurrencyCode,
    decimal Rate,
    DateOnly RateDate,
    ExchangeRateSource Source);
