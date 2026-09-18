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
    public string JobType => ExchangeRateSyncJob.Type;

    public async Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken)
    {
        if (!JobPayload.TryRead<ExchangeRateSyncJobPayload>(payload, out var request))
        {
            return ProcessOutput.New.WithError(BackgroundJobMessages.PayloadInvalid);
        }

        try
        {
            var sourceCurrencies = options.Currencies
                .Where(code => code != "BRL")
                .ToArray();
            var batch = await client.GetLatestQuotesAsync(
                sourceCurrencies,
                request.RequestedDate,
                cancellationToken);
            var rejected = batch.Quotes.Count(quote => quote.BrlPerUnit <= 0);
            var validQuotes = batch.Quotes
                .Where(quote => quote.BrlPerUnit > 0)
                .Append(new PtaxQuote("BRL", 1m))
                .GroupBy(quote => quote.CurrencyCode, StringComparer.Ordinal)
                .Select(group => group.Single())
                .ToArray();
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
            var result = await rates.UpsertPublishedAsync(candidates, cancellationToken);

            logger.LogInformation(
                "Exchange-rate synchronization stored {StoredCount} rates, left {UnchangedCount} unchanged, and rejected {RejectedCount} source rows",
                result.StoredCount,
                result.UnchangedCount,
                rejected);

            return ProcessOutput.New;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The exchange-rate source is unavailable");

            return ProcessOutput.New.WithError(ExchangeRateSyncMessages.SourceUnavailable);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The exchange-rate source timed out");

            return ProcessOutput.New.WithError(ExchangeRateSyncMessages.SourceUnavailable);
        }
    }
}
