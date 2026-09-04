using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.Integration.Rates;

public sealed class PtaxRateClient(
    HttpClient httpClient,
    IRateLimitDelay delay) : IPtaxRateClient
{
    private const int LookbackDays = 7;
    private const int MaximumAttempts = 4;

    public async Task<PtaxQuoteBatch> GetLatestQuotesAsync(
        IReadOnlyCollection<string> currencyCodes,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        if (currencyCodes.Count == 0)
        {
            return new PtaxQuoteBatch(requestedDate, []);
        }

        var histories = new Dictionary<string, IReadOnlyCollection<PtaxPublication>>(StringComparer.Ordinal);
        foreach (var code in currencyCodes.Distinct(StringComparer.Ordinal))
        {
            histories[code] = await ReadHistoryAsync(code, requestedDate, cancellationToken);
        }

        var commonDates = histories.Values
            .Select(history => history.Select(publication => publication.Date).ToHashSet())
            .Aggregate((left, right) =>
            {
                left.IntersectWith(right);
                return left;
            });
        var publicationDate = commonDates
            .Where(date => date <= requestedDate)
            .OrderDescending()
            .FirstOrDefault();
        if (publicationDate == default)
        {
            throw new InvalidOperationException(ExchangeRateSyncMessages.PublicationUnavailable);
        }

        var quotes = histories.Select(entry =>
        {
            var publication = entry.Value
                .Where(item => item.Date == publicationDate)
                .OrderByDescending(item => item.IsClosing)
                .ThenByDescending(item => item.Timestamp)
                .First();
            return new PtaxQuote(entry.Key, publication.BrlPerUnit);
        }).ToArray();

        return new PtaxQuoteBatch(publicationDate, quotes);
    }

    private async Task<IReadOnlyCollection<PtaxPublication>> ReadHistoryAsync(
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
        using var response = await SendWithBackoffAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<PtaxDocument>(cancellationToken)
            ?? throw new InvalidOperationException(ExchangeRateSyncMessages.PublicationUnavailable);

        return document.Value.Select(item => new PtaxPublication(
            DateOnly.ParseExact(item.Timestamp[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(item.Timestamp, CultureInfo.InvariantCulture),
            item.SellRate,
            string.Equals(item.BulletinType, "Fechamento PTAX", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private async Task<HttpResponseMessage> SendWithBackoffAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await httpClient.GetAsync(path, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == MaximumAttempts)
            {
                return response;
            }

            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt);
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
