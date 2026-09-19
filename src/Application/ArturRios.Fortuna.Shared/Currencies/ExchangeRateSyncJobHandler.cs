using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Shared.Currencies;

public sealed class ExchangeRateSyncJobHandler(
    IPtaxRateClient client,
    IExchangeRateStore rates,
    RateSyncOptions options,
    ILogger<ExchangeRateSyncJobHandler> logger) : IBackgroundJobHandler
{
    private const string BaseCurrency = "BRL";

    public string JobType => ExchangeRateSyncJob.Type;

    public async Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        if (!JobPayload.TryRead<ExchangeRateSyncJobPayload>(payload, out var request))
        {
            return ProcessOutput.New.WithError(BackgroundJobMessages.PayloadInvalid);
        }

        var sourceCurrencies = options.Currencies
            .Select(code => code.Trim().ToUpperInvariant())
            .Where(code => code.Length > 0 && code != BaseCurrency)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = await client.GetLatestQuotesAsync(
            sourceCurrencies,
            request.RequestedDate,
            cancellationToken);
        if (result.Outcome != PtaxQuoteOutcome.Succeeded || result.Batch is null)
        {
            var reason = result.Outcome == PtaxQuoteOutcome.PublicationUnavailable
                ? ExchangeRateSyncMessages.PublicationUnavailable
                : ExchangeRateSyncMessages.SourceUnavailable;
            logger.LogWarning("Exchange-rate synchronization failed: {Reason}", reason);

            return ProcessOutput.New.WithError(reason);
        }

        var batch = result.Batch;
        var rejected = batch.Quotes.Count(quote => quote.BrlPerUnit <= 0);
        var groups = batch.Quotes
            .Where(quote => quote.BrlPerUnit > 0)
            .Select(quote => quote with { CurrencyCode = quote.CurrencyCode.Trim().ToUpperInvariant() })
            .Where(quote => quote.CurrencyCode != BaseCurrency)
            .Append(new PtaxQuote(BaseCurrency, 1m))
            .GroupBy(quote => quote.CurrencyCode, StringComparer.Ordinal)
            .ToArray();
        // A source that repeats a currency must not crash the run; the first row wins.
        var duplicates = groups.Sum(group => group.Count() - 1);
        var validQuotes = groups.Select(group => group.First()).ToArray();
        var candidates = (
            from baseQuote in validQuotes
            from quote in validQuotes
            where baseQuote.CurrencyCode != quote.CurrencyCode
            select new PublishedRateCandidate(
                baseQuote.CurrencyCode,
                quote.CurrencyCode,
                baseQuote.BrlPerUnit / quote.BrlPerUnit,
                batch.PublicationDate))
            .ToArray();
        var stored = await rates.UpsertPublishedAsync(candidates, cancellationToken);
        if (stored.MissingCurrencyCodes is { Count: > 0 } unknown)
        {
            logger.LogWarning(
                "Exchange-rate synchronization skipped {SkippedCount} rates for currencies missing from the reference set: {CurrencyCodes}",
                stored.SkippedCount,
                string.Join(", ", unknown));
        }

        var published = validQuotes.Select(quote => quote.CurrencyCode).ToHashSet(StringComparer.Ordinal);
        var missing = sourceCurrencies.Where(code => !published.Contains(code)).ToArray();
        logger.LogInformation(
            "Exchange-rate synchronization stored {StoredCount} rates, left {UnchangedCount} unchanged, rejected {RejectedCount} source rows, ignored {DuplicateCount} duplicates and found no rate for {MissingCurrencies}",
            stored.StoredCount,
            stored.UnchangedCount,
            rejected,
            duplicates,
            missing);
        var output = ProcessOutput.New;
        if (missing.Length > 0)
        {
            output.AddMessage(ExchangeRateSyncMessages.CurrenciesMissing(missing));
        }

        return output;
    }
}
