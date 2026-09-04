namespace ArturRios.Fortuna.Shared.Currencies;

public interface IExchangeRateStore
{
    Task<PublishedRateUpsertResult> UpsertPublishedAsync(
        IReadOnlyCollection<PublishedRateCandidate> rates,
        CancellationToken cancellationToken);

    Task<ManualRateUpsertResult> UpsertManualAsync(
        ManualRateCandidate rate,
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

public sealed record ManualRateCandidate(
    string BaseCurrencyCode,
    string QuoteCurrencyCode,
    decimal Rate,
    DateOnly RateDate);

public sealed record ManualRateUpsertResult(
    decimal Rate,
    bool ReplacedExisting);
