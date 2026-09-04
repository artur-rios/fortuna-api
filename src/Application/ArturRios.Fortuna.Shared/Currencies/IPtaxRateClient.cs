namespace ArturRios.Fortuna.Shared.Currencies;

public interface IPtaxRateClient
{
    Task<PtaxQuoteBatch> GetLatestQuotesAsync(
        IReadOnlyCollection<string> currencyCodes,
        DateOnly requestedDate,
        CancellationToken cancellationToken);
}

public sealed record PtaxQuoteBatch(
    DateOnly PublicationDate,
    IReadOnlyCollection<PtaxQuote> Quotes);

public sealed record PtaxQuote(
    string CurrencyCode,
    decimal BrlPerUnit);
