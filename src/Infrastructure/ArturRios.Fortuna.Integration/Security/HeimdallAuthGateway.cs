using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArturRios.Fortuna.Shared.Security;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Integration.Security;

public sealed class HeimdallAuthGateway(
    HttpClient httpClient,
    ILogger<HeimdallAuthGateway>? logger = null) : IHeimdallAuthGateway
{
    public Task<HeimdallAuthResult<HeimdallLoginResult>> LoginAsync(
        string email, string password, Guid scopeId, CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallLoginResult>(HttpMethod.Post,
            "api/auth/login", new { email, password, scopeId }, null, cancellationToken);

    public Task<HeimdallAuthResult<HeimdallGoogleSignInResult>> GoogleSignInAsync(
        string idToken, Guid scopeId, CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallGoogleSignInResult>(HttpMethod.Post,
            "api/auth/google", new { idToken, scopeId }, null, cancellationToken);

    public Task<HeimdallAuthResult<HeimdallTwoFactorVerificationResult>> VerifyTwoFactorAsync(
        string challengeToken, string? code, string? recoveryCode, CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallTwoFactorVerificationResult>(HttpMethod.Post,
            "api/auth/2fa/verify", new { challengeToken, code, recoveryCode }, null, cancellationToken);

    public Task<HeimdallAuthResult<object>> GoogleSignOutAsync(
        string bearerToken, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/google/sign-out", new { }, bearerToken, cancellationToken);

    public Task<HeimdallAuthResult<object>> RequestPasswordRecoveryAsync(
        string email, Guid scopeId, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/password-recovery", new { email, scopeId }, null, cancellationToken);

    public Task<HeimdallAuthResult<object>> ResetPasswordAsync(
        string token, string newPassword, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/password-reset", new { token, newPassword }, null, cancellationToken);

    public Task<HeimdallAuthResult<object>> VerifyEmailAsync(
        string token, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/verify-email", new { token }, null, cancellationToken);

    public Task<HeimdallAuthResult<object>> ResendVerificationAsync(
        string bearerToken, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/resend-verification", new { }, bearerToken, cancellationToken);

    public Task<HeimdallAuthResult<HeimdallTwoFactorStatusResult>> GetTwoFactorStatusAsync(
        string bearerToken, CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallTwoFactorStatusResult>(HttpMethod.Get,
            "api/auth/2fa", null, bearerToken, cancellationToken);

    public Task<HeimdallAuthResult<HeimdallTwoFactorSetupResult>> EnableTwoFactorAsync(
        IReadOnlyCollection<string> methods, string bearerToken, CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallTwoFactorSetupResult>(HttpMethod.Post,
            "api/auth/2fa/enable", new { methods }, bearerToken, cancellationToken);

    public Task<HeimdallAuthResult<HeimdallRecoveryCodesResult>> ConfirmTwoFactorAsync(
        string? appCode, string? emailCode, string bearerToken, CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallRecoveryCodesResult>(HttpMethod.Post,
            "api/auth/2fa/confirm", new { appCode, emailCode }, bearerToken, cancellationToken);

    public Task<HeimdallAuthResult<HeimdallTwoFactorDisabledResult>> DisableTwoFactorAsync(
        string password, string? code, string? recoveryCode, string bearerToken,
        CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallTwoFactorDisabledResult>(HttpMethod.Post,
            "api/auth/2fa/disable", new { password, code, recoveryCode }, bearerToken, cancellationToken);

    public Task<HeimdallAuthResult<HeimdallRecoveryCodesResult>> RegenerateRecoveryCodesAsync(
        string? code, string? recoveryCode, string bearerToken, CancellationToken cancellationToken) =>
        SendAsync<object, HeimdallRecoveryCodesResult>(HttpMethod.Post,
            "api/auth/2fa/recovery-codes/regenerate", new { code, recoveryCode }, bearerToken,
            cancellationToken);

    private async Task<HeimdallAuthResult<TResponse>> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest? payload,
        string? bearerToken,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (payload is not null)
            {
                request.Content = JsonContent.Create(payload);
            }
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadFromJsonAsync<HeimdallEnvelope<TResponse>>(
                    cancellationToken);
                if (envelope?.Data is null)
                {
                    logger?.LogWarning(
                        "Heimdall request to {Path} returned a successful response without data.",
                        path);
                    return new(HeimdallAuthOutcome.Unavailable);
                }

                return new(HeimdallAuthOutcome.Succeeded, envelope.Data);
            }

            logger?.LogWarning(
                "Heimdall request to {Path} failed with status {StatusCode}.",
                path,
                (int)response.StatusCode);

            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest => new(HeimdallAuthOutcome.InvalidRequest),
                HttpStatusCode.NotFound => new(HeimdallAuthOutcome.NotFound),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new(HeimdallAuthOutcome.Rejected),
                _ => new(HeimdallAuthOutcome.Unavailable)
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            JsonException or NotSupportedException)
        {
            logger?.LogWarning(
                "Heimdall request to {Path} could not be completed ({FailureType}).",
                path,
                exception.GetType().Name);
            return new(HeimdallAuthOutcome.Unavailable);
        }
    }

    private sealed class HeimdallEnvelope<T>
    {
        public T? Data { get; init; }
    }
}
