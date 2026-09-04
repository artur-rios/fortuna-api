namespace ArturRios.Fortuna.Shared.Currencies;

public interface IExchangeRateStore
{
    Task<PublishedRateUpsertResult> UpsertPublishedAsync(
        IReadOnlyCollection<PublishedRateCandidate> rates,
        CancellationToken cancellationToken);
}

public sealed record PublishedRateCandidate(
    string BaseCurrencyCode,
    string QuoteCurrencyCode,
    decimal Rate,
    DateOnly PublicationDate);

public sealed record PublishedRateUpsertResult(
    int StoredCount,
    int UnchangedCount);
