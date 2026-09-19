using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ArturRios.Fortuna.Integration.Ingestion;

internal enum PluggyApiKeyOutcome
{
    Issued = 1,
    NotConfigured = 2,
    Rejected = 3,
    Unavailable = 4
}

internal sealed record PluggyApiKeyResult(PluggyApiKeyOutcome Outcome, string? ApiKey = null);

/// <summary>
/// Exchanges the application's ClientId/ClientSecret for an API key (POST /auth). Pluggy API keys
/// expire after two hours, so every server-side operation requests a fresh one instead of reusing
/// a stored key.
/// </summary>
internal static class PluggyApiKeyClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<PluggyApiKeyResult> RequestAsync(
        HttpClient client,
        PluggySourceOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.ClientSecret) ||
            options.BaseUri is null)
        {
            return new PluggyApiKeyResult(PluggyApiKeyOutcome.NotConfigured);
        }

        using var response = await client.PostAsJsonAsync(
            "auth",
            new AuthenticationRequest(options.ClientId, options.ClientSecret),
            JsonOptions,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new PluggyApiKeyResult(PluggyApiKeyOutcome.Rejected);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new PluggyApiKeyResult(PluggyApiKeyOutcome.Unavailable);
        }

        var credential = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(
            JsonOptions,
            cancellationToken);
        // Pluggy documents the field as apiKey; accessToken is accepted for older responses.
        var apiKey = credential?.ApiKey ?? credential?.AccessToken;

        return string.IsNullOrWhiteSpace(apiKey)
            ? new PluggyApiKeyResult(PluggyApiKeyOutcome.Unavailable)
            : new PluggyApiKeyResult(PluggyApiKeyOutcome.Issued, apiKey);
    }

    private sealed record AuthenticationRequest(string ClientId, string ClientSecret);

    private sealed record AuthenticationResponse(string? ApiKey, string? AccessToken);
}
