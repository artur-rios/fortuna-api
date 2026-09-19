namespace ArturRios.Fortuna.Shared.Currencies;

public interface IPtaxRateClient
{
    /// <summary>
    /// Reads the latest common publication for the currencies. An unreachable source or a missing
    /// publication is reported through the result's outcome, not thrown.
    /// </summary>
    Task<PtaxQuoteResult> GetLatestQuotesAsync(
        IReadOnlyCollection<string> currencyCodes,
        DateOnly requestedDate,
        CancellationToken cancellationToken);
}

public enum PtaxQuoteOutcome
{
    Succeeded = 1,
    SourceUnavailable = 2,
    PublicationUnavailable = 3
}

public sealed record PtaxQuoteResult(PtaxQuoteOutcome Outcome, PtaxQuoteBatch? Batch = null)
{
    public static PtaxQuoteResult Succeeded(PtaxQuoteBatch batch) =>
        new(PtaxQuoteOutcome.Succeeded, batch ?? throw new ArgumentNullException(nameof(batch)));

    public static PtaxQuoteResult Failed(PtaxQuoteOutcome outcome) => new(outcome);
}

public sealed record PtaxQuoteBatch(
    DateOnly PublicationDate,
    IReadOnlyCollection<PtaxQuote> Quotes)
{
    /// <summary>Requested currencies the source published nothing for in the lookback window.</summary>
    public IReadOnlyCollection<string> MissingCurrencies { get; init; } = [];
}

public sealed record PtaxQuote(
    string CurrencyCode,
    decimal BrlPerUnit);
