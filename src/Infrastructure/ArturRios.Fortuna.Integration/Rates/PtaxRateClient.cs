using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArturRios.Fortuna.Shared.Currencies;

namespace ArturRios.Fortuna.Integration.Rates;

public sealed class PtaxRateClient(
    HttpClient httpClient,
    IRateLimitDelay delay,
    TimeProvider timeProvider) : IPtaxRateClient
{
    private const int LookbackDays = 7;

    public async Task<PtaxQuoteResult> GetLatestQuotesAsync(
        IReadOnlyCollection<string> currencyCodes,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        if (currencyCodes.Count == 0)
        {
            return PtaxQuoteResult.Succeeded(new PtaxQuoteBatch(requestedDate, []));
        }

        try
        {
            var histories = new Dictionary<string, IReadOnlyCollection<PtaxPublication>>(StringComparer.Ordinal);
            foreach (var code in currencyCodes
                         .Select(code => code.Trim().ToUpperInvariant())
                         .Distinct(StringComparer.Ordinal))
            {
                var history = await ReadHistoryAsync(code, requestedDate, cancellationToken);
                if (history is null)
                {
                    return PtaxQuoteResult.Failed(PtaxQuoteOutcome.SourceUnavailable);
                }

                histories[code] = history;
            }

            return Select(histories, requestedDate);
        }
        catch (HttpRequestException)
        {
            return PtaxQuoteResult.Failed(PtaxQuoteOutcome.SourceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation.
            return PtaxQuoteResult.Failed(PtaxQuoteOutcome.SourceUnavailable);
        }
        catch (JsonException)
        {
            return PtaxQuoteResult.Failed(PtaxQuoteOutcome.SourceUnavailable);
        }
    }

    private static PtaxQuoteResult Select(
        IReadOnlyDictionary<string, IReadOnlyCollection<PtaxPublication>> histories,
        DateOnly requestedDate)
    {
        var published = histories.Where(entry => entry.Value.Count > 0).ToArray();
        var missing = histories.Where(entry => entry.Value.Count == 0)
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (published.Length == 0)
        {
            return PtaxQuoteResult.Failed(PtaxQuoteOutcome.PublicationUnavailable);
        }

        var commonDates = published
            .Select(entry => entry.Value.Select(publication => publication.Date).ToHashSet())
            .Aggregate((left, right) =>
            {
                left.IntersectWith(right);

                return left;
            });
        var candidates = commonDates.Where(date => date <= requestedDate).ToArray();
        if (candidates.Length == 0)
        {
            return PtaxQuoteResult.Failed(PtaxQuoteOutcome.PublicationUnavailable);
        }

        var publicationDate = candidates.Max();
        var quotes = published.Select(entry =>
        {
            var publication = entry.Value
                .Where(item => item.Date == publicationDate)
                .OrderByDescending(item => item.IsClosing)
                .ThenByDescending(item => item.Timestamp)
                .First();

            return new PtaxQuote(entry.Key, publication.BrlPerUnit);
        }).ToArray();

        return PtaxQuoteResult.Succeeded(new PtaxQuoteBatch(publicationDate, quotes)
        {
            MissingCurrencies = missing
        });
    }

    /// <summary>Reads one currency's publications; null when the source answered with an error.</summary>
    private async Task<IReadOnlyCollection<PtaxPublication>?> ReadHistoryAsync(
        string currencyCode,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        var startDate = requestedDate.AddDays(-LookbackDays);
        var path = "CotacaoMoedaPeriodo(moeda=@moeda,dataInicial=@dataInicial,dataFinalCotacao=@dataFinalCotacao)" +
            $"?%40moeda=%27{Uri.EscapeDataString(currencyCode)}%27" +
            $"&%40dataInicial=%27{startDate:MM-dd-yyyy}%27" +
            $"&%40dataFinalCotacao=%27{requestedDate:MM-dd-yyyy}%27" +
            "&%24format=json";
        using var response = await SendWithRetryAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var document = await response.Content.ReadFromJsonAsync<PtaxDocument>(cancellationToken);
        if (document is null)
        {
            return null;
        }

        var publications = new List<PtaxPublication>();
        foreach (var item in document.Value)
        {
            // A row with an unreadable timestamp is skipped rather than failing the whole read.
            if (item.Timestamp.Length >= 10 &&
                DateOnly.TryParseExact(item.Timestamp[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) &&
                DateTimeOffset.TryParse(item.Timestamp, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var timestamp))
            {
                publications.Add(new PtaxPublication(
                    date,
                    timestamp,
                    item.SellRate,
                    string.Equals(item.BulletinType, "Fechamento PTAX", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return publications;
    }

    /// <summary>Retries rate limiting and transient server errors a bounded number of times.</summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await httpClient.GetAsync(path, cancellationToken);
            if (!HttpRetryPolicy.IsTransient(response.StatusCode) ||
                attempt == HttpRetryPolicy.MaximumAttempts)
            {
                return response;
            }

            var retryAfter = HttpRetryPolicy.RetryDelay(response, attempt, timeProvider.GetUtcNow());
            response.Dispose();
            await delay.WaitAsync(retryAfter, cancellationToken);
        }
    }

    private sealed class PtaxDocument
    {
        [JsonPropertyName("value")]
        public PtaxItem[] Value { get; init; } = [];
    }

    private sealed class PtaxItem
    {
        [JsonPropertyName("cotacaoVenda")]
        public decimal SellRate { get; init; }

        [JsonPropertyName("dataHoraCotacao")]
        public string Timestamp { get; init; } = string.Empty;

        [JsonPropertyName("tipoBoletim")]
        public string BulletinType { get; init; } = string.Empty;
    }

    private sealed record PtaxPublication(
        DateOnly Date,
        DateTimeOffset Timestamp,
        decimal BrlPerUnit,
        bool IsClosing);
}
