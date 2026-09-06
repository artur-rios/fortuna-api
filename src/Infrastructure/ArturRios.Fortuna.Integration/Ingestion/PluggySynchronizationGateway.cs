using System.Globalization;
using System.Net;
using System.Text.Json;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Integration.Rates;
using ArturRios.Fortuna.Shared.Ingestion;

namespace ArturRios.Fortuna.Integration.Ingestion;

public sealed class PluggySynchronizationGateway(
    HttpClient client,
    PluggySourceOptions options,
    IRateLimitDelay delay) : IPluggySynchronizationGateway
{
    private const int PageSize = 500;
    private const int MaximumAttempts = 4;

    public async Task<PluggySynchronizationFetchResult> FetchAsync(
        string externalReference,
        string accessToken,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        CancellationToken cancellationToken)
    {
        if (!options.IsNetworkAvailable || options.BaseUri is null ||
            string.IsNullOrWhiteSpace(accessToken))
        {
            return Result(PluggySynchronizationFetchOutcome.Unavailable);
        }

        try
        {
            var item = await ReadAsync(
                $"items/{Uri.EscapeDataString(externalReference)}",
                accessToken,
                cancellationToken);
            if (item.Outcome != PluggySynchronizationFetchOutcome.Succeeded)
            {
                return Result(item.Outcome);
            }

            using var itemDocument = item.Document!;
            var institution = ReadPath(itemDocument.RootElement, "connector", "name") ?? "Pluggy";
            var resources = new List<PluggyResourceRecord>();
            var transactions = new List<PluggyTransactionRecord>();
            var accounts = await ReadPagesAsync(
                $"accounts?itemId={Uri.EscapeDataString(externalReference)}",
                accessToken,
                cancellationToken);
            if (accounts.Outcome != PluggySynchronizationFetchOutcome.Succeeded)
            {
                return Result(accounts.Outcome);
            }

            foreach (var account in accounts.Items)
            {
                var accountId = ReadString(account, "id");
                if (string.IsNullOrWhiteSpace(accountId))
                {
                    continue;
                }

                resources.Add(ToResource(account, accountId, institution));
                var path = BuildTransactionPath(accountId, periodStart, periodEnd);
                var transactionPage = await ReadPagesAsync(path, accessToken, cancellationToken);
                if (transactionPage.Outcome != PluggySynchronizationFetchOutcome.Succeeded)
                {
                    return Result(transactionPage.Outcome);
                }

                transactions.AddRange(transactionPage.Items.Select(transaction =>
                    ToTransaction(transaction, accountId)));
            }

            return new PluggySynchronizationFetchResult(
                PluggySynchronizationFetchOutcome.Succeeded,
                new PluggySynchronizationBatch(resources, transactions));
        }
        catch (HttpRequestException)
        {
            return Result(PluggySynchronizationFetchOutcome.Unavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result(PluggySynchronizationFetchOutcome.Unavailable);
        }
        catch (JsonException)
        {
            return Result(PluggySynchronizationFetchOutcome.Unavailable);
        }
        catch (FormatException)
        {
            return Result(PluggySynchronizationFetchOutcome.Unavailable);
        }
    }

    private async Task<PageResult> ReadPagesAsync(
        string path,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        for (var page = 1; ; page++)
        {
            var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var response = await ReadAsync(
                $"{path}{separator}page={page}&pageSize={PageSize}",
                accessToken,
                cancellationToken);
            if (response.Outcome != PluggySynchronizationFetchOutcome.Succeeded)
            {
                response.Document?.Dispose();
                return new PageResult(response.Outcome, []);
            }

            using var document = response.Document!;
            if (!document.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("The Pluggy response does not contain a results array.");
            }

            var pageItems = results.EnumerateArray().Select(item => item.Clone()).ToArray();
            items.AddRange(pageItems);
            var total = ReadInt32(document.RootElement, "total") ?? items.Count;
            if (pageItems.Length == 0 || items.Count >= total || pageItems.Length < PageSize)
            {
                return new PageResult(PluggySynchronizationFetchOutcome.Succeeded, items);
            }
        }
    }

    private async Task<DocumentResult> ReadAsync(
        string path,
        string accessToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-API-KEY", accessToken);
            var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaximumAttempts)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt);
                response.Dispose();
                await delay.WaitAsync(retryAfter, cancellationToken);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or
                HttpStatusCode.NotFound)
            {
                response.Dispose();
                return new DocumentResult(
                    PluggySynchronizationFetchOutcome.RequiresReauthentication,
                    null);
            }

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                return new DocumentResult(PluggySynchronizationFetchOutcome.Unavailable, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            response.Dispose();
            return new DocumentResult(PluggySynchronizationFetchOutcome.Succeeded, document);
        }
    }

    private static PluggyResourceRecord ToResource(
        JsonElement account,
        string externalReference,
        string institution)
    {
        var type = ReadString(account, "type") ?? string.Empty;
        var subtype = ReadString(account, "subtype") ?? string.Empty;
        var isCard = type.Equals("CREDIT", StringComparison.OrdinalIgnoreCase) ||
            subtype.Contains("CREDIT", StringComparison.OrdinalIgnoreCase);
        var number = ReadString(account, "number");
        var lastFour = number is { Length: >= 4 } ? number[^4..] : null;
        return new PluggyResourceRecord(
            externalReference,
            isCard ? PluggyResourceKind.CreditCard : PluggyResourceKind.Account,
            ReadString(account, "name") ?? (isCard ? "Imported card" : "Imported account"),
            institution,
            ReadString(account, "currencyCode") ?? "BRL",
            ReadDecimal(account, "balance") ?? 0m,
            ReadPathDecimal(account, "creditData", "creditLimit"),
            ReadPathInt16(account, "creditData", "balanceCloseDate"),
            ReadPathInt16(account, "creditData", "balanceDueDate"),
            lastFour);
    }

    private static PluggyTransactionRecord ToTransaction(JsonElement transaction, string accountId)
    {
        var amount = ReadDecimal(transaction, "amount");
        var type = ReadString(transaction, "type");
        var direction = type?.Equals("CREDIT", StringComparison.OrdinalIgnoreCase) == true
            ? TransactionDirection.Earning
            : type?.Equals("DEBIT", StringComparison.OrdinalIgnoreCase) == true
                ? TransactionDirection.Expense
                : (TransactionDirection?)null;
        return new PluggyTransactionRecord(
            transaction.GetRawText(),
            ReadString(transaction, "accountId") ?? accountId,
            ReadString(transaction, "id"),
            direction,
            amount.HasValue ? Math.Abs(amount.Value) : null,
            ReadDate(transaction, "date"),
            ReadString(transaction, "description"),
            ReadPath(transaction, "category", "description") ?? ReadString(transaction, "category"));
    }

    private static string BuildTransactionPath(
        string accountId,
        DateOnly? periodStart,
        DateOnly? periodEnd)
    {
        var path = $"transactions?accountId={Uri.EscapeDataString(accountId)}";
        if (periodStart.HasValue)
        {
            path += $"&from={periodStart:yyyy-MM-dd}";
        }

        if (periodEnd.HasValue)
        {
            path += $"&to={periodEnd:yyyy-MM-dd}";
        }

        return path;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? ReadPath(JsonElement element, string parent, string child) =>
        element.TryGetProperty(parent, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadString(nested, child)
            : null;

    private static decimal? ReadDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.TryGetDecimal(out var value)
            ? value
            : null;

    private static decimal? ReadPathDecimal(JsonElement element, string parent, string child) =>
        element.TryGetProperty(parent, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadDecimal(nested, child)
            : null;

    private static short? ReadPathInt16(JsonElement element, string parent, string child)
    {
        var value = ReadPath(element, parent, child);
        if (value is not null && DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var date))
        {
            return (short)date.Day;
        }

        return short.TryParse(value, CultureInfo.InvariantCulture, out var day) ? day : null;
    }

    private static int? ReadInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private static DateOnly? ReadDate(JsonElement element, string name)
    {
        var value = ReadString(element, name);
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var date) ? date : null;
    }

    private static PluggySynchronizationFetchResult Result(
        PluggySynchronizationFetchOutcome outcome) => new(outcome);

    private sealed record DocumentResult(
        PluggySynchronizationFetchOutcome Outcome,
        JsonDocument? Document);

    private sealed record PageResult(
        PluggySynchronizationFetchOutcome Outcome,
        IReadOnlyCollection<JsonElement> Items);
}
