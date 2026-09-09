using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Shared.Security;

namespace ArturRios.Fortuna.Integration.Security;

public sealed class HeimdallAuthGateway(HttpClient httpClient) : IHeimdallAuthGateway
{
    public Task<HeimdallAuthResult<HeimdallLoginResult>> LoginAsync(
        string email, string password, Guid scopeId, CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallLoginResult>(
            "api/auth/login", new { email, password, scopeId }, null, cancellationToken);

    public Task<HeimdallAuthResult<HeimdallGoogleSignInResult>> GoogleSignInAsync(
        string idToken, Guid scopeId, CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallGoogleSignInResult>(
            "api/auth/google", new { idToken, scopeId }, null, cancellationToken);

    public Task<HeimdallAuthResult<HeimdallTwoFactorVerificationResult>> VerifyTwoFactorAsync(
        string challengeToken, string? code, string? recoveryCode, CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallTwoFactorVerificationResult>(
            "api/auth/2fa/verify", new { challengeToken, code, recoveryCode }, null, cancellationToken);

    public Task<HeimdallAuthResult<object>> GoogleSignOutAsync(
        string bearerToken, CancellationToken cancellationToken) =>
        SendAsync<object, object>(
            "api/auth/google/sign-out", new { }, bearerToken, cancellationToken);

    private async Task<HeimdallAuthResult<TResponse>> SendAsync<TRequest, TResponse>(
        string path,
        TRequest payload,
        string? bearerToken,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(payload)
            };
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadFromJsonAsync<HeimdallEnvelope<TResponse>>(
                    cancellationToken);
                return envelope?.Data is null
                    ? new(HeimdallAuthOutcome.Unavailable)
                    : new(HeimdallAuthOutcome.Succeeded, envelope.Data);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest => new(HeimdallAuthOutcome.InvalidRequest),
                >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError =>
                    new(HeimdallAuthOutcome.Rejected),
                _ => new(HeimdallAuthOutcome.Unavailable)
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new(HeimdallAuthOutcome.Unavailable);
        }
    }

    private sealed class HeimdallEnvelope<T>
    {
        public T? Data { get; init; }
    }
}
