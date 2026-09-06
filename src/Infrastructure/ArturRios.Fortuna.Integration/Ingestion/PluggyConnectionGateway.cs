using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArturRios.Fortuna.Shared.Ingestion;

namespace ArturRios.Fortuna.Integration.Ingestion;

public sealed class PluggyConnectionGateway(
    HttpClient client,
    PluggySourceOptions options) : IPluggyConnectionGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PluggyConnectionValidation> ValidateAsync(
        string externalReference,
        CancellationToken cancellationToken)
    {
        if (!options.IsNetworkAvailable)
        {
            return Result(PluggyConnectionValidationOutcome.Unavailable);
        }

        if (string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.ClientSecret) ||
            options.BaseUri is null)
        {
            return Result(PluggyConnectionValidationOutcome.NotConfigured);
        }

        try
        {
            using var authentication = await client.PostAsJsonAsync(
                "auth",
                new AuthenticationRequest(options.ClientId, options.ClientSecret),
                JsonOptions,
                cancellationToken);
            if (authentication.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Result(PluggyConnectionValidationOutcome.NotConfigured);
            }

            if (!authentication.IsSuccessStatusCode)
            {
                return Result(PluggyConnectionValidationOutcome.Unavailable);
            }

            var credential = await authentication.Content.ReadFromJsonAsync<AuthenticationResponse>(
                JsonOptions,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(credential?.AccessToken))
            {
                return Result(PluggyConnectionValidationOutcome.Unavailable);
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"items/{Uri.EscapeDataString(externalReference)}");
            request.Headers.Add("X-API-KEY", credential.AccessToken);
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
                return Result(PluggyConnectionValidationOutcome.InvalidReference);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result(PluggyConnectionValidationOutcome.Unavailable);
            }

            var item = await response.Content.ReadFromJsonAsync<ItemResponse>(
                JsonOptions,
                cancellationToken);
            return item is null ||
                !string.Equals(item.Id, externalReference, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(item.Connector?.Name)
                ? Result(PluggyConnectionValidationOutcome.InvalidReference)
                : new PluggyConnectionValidation(
                    PluggyConnectionValidationOutcome.Succeeded,
                    item.Connector.Name.Trim(),
                    credential.AccessToken);
        }
        catch (HttpRequestException)
        {
            return Result(PluggyConnectionValidationOutcome.Unavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result(PluggyConnectionValidationOutcome.Unavailable);
        }
        catch (JsonException)
        {
            return Result(PluggyConnectionValidationOutcome.Unavailable);
        }
    }

    private static PluggyConnectionValidation Result(PluggyConnectionValidationOutcome outcome) =>
        new(outcome);

    private sealed record AuthenticationRequest(string ClientId, string ClientSecret);
    private sealed record AuthenticationResponse(string? AccessToken);
    private sealed record ItemResponse(string? Id, ConnectorResponse? Connector);
    private sealed record ConnectorResponse(string? Name);
}
