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
    private static readonly JsonSerializerOptions ErrorJsonOptions = new(JsonSerializerDefaults.Web);

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

    public Task<HeimdallAuthResult<object>> ResendTwoFactorChallengeCodeAsync(
        string challengeToken, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/2fa/challenge/resend", new { challengeToken }, null, cancellationToken);

    public Task<HeimdallAuthResult<object>> GoogleSignOutAsync(
        string bearerToken, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/google/sign-out", new { }, bearerToken, cancellationToken);

    public Task<HeimdallAuthResult<object>> RequestPasswordRecoveryAsync(
        string email, Guid scopeId, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/password-recovery", new { email, scopeId }, null, cancellationToken);

    public Task<HeimdallAuthResult<object>> RegisterAsync(
        string name, string email, string password, Guid scopeId, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/register", new { name, email, password, scopeId }, null, cancellationToken,
            forbidden: HeimdallAuthOutcome.Closed);

    public Task<HeimdallAuthResult<object>> ResetPasswordAsync(
        string token, string newPassword, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/password-reset", new { token, newPassword }, null, cancellationToken);

    public Task<HeimdallAuthResult<object>> VerifyEmailAsync(
        string token, string? password, CancellationToken cancellationToken) =>
        SendAsync<object, object>(HttpMethod.Post,
            "api/auth/verify-email", new { token, password }, null, cancellationToken,
            forbidden: HeimdallAuthOutcome.Unavailable,
            unprocessable: HeimdallAuthOutcome.PasswordRequired);

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
        CancellationToken cancellationToken,
        HeimdallAuthOutcome forbidden = HeimdallAuthOutcome.Rejected,
        HeimdallAuthOutcome unprocessable = HeimdallAuthOutcome.Unavailable)
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

            // Heimdall's 429 and 409 are answers for the caller, not failures to hide: their message is
            // passed on, and a 429's Retry-After with it. Every other refusal keeps its upstream text out.
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new(HeimdallAuthOutcome.RateLimited)
                {
                    Message = await ErrorMessageAsync(response, cancellationToken),
                    RetryAfterSeconds = RetryAfterSeconds(response)
                };
            }
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return new(HeimdallAuthOutcome.Conflict)
                {
                    Message = await ErrorMessageAsync(response, cancellationToken)
                };
            }

            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest => new(HeimdallAuthOutcome.InvalidRequest),
                HttpStatusCode.NotFound => new(HeimdallAuthOutcome.NotFound),
                HttpStatusCode.Unauthorized => new(HeimdallAuthOutcome.Rejected),
                // A refusal on most routes; on registration, a scope that is closed to it (UC-81).
                // Verify-email passes Unavailable: Heimdall never answers it 403, so one comes from
                // something in front of Heimdall and must not read as a wrong sign-up password.
                HttpStatusCode.Forbidden => new(forbidden),
                // Mapped per route; verify-email passes PasswordRequired, every other route Unavailable.
                HttpStatusCode.UnprocessableEntity => new(unprocessable),
                _ => new(HeimdallAuthOutcome.Unavailable)
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            JsonException or NotSupportedException)
        {
            // A refusal by the plain-HTTP guard is a configuration problem, not Heimdall being down, so
            // it is named. Only the fixed refusal is logged: the handler's own message adds the address.
            if (IsPrivateNetworkRefusal(exception))
            {
                logger?.LogWarning(
                    "Heimdall request to {Path} was refused by the private-network guard: {Refusal}",
                    path,
                    PrivateNetworkConnector.RefusedMessage);
            }
            else
            {
                logger?.LogWarning(
                    "Heimdall request to {Path} could not be completed ({FailureType}).",
                    path,
                    exception.GetType().Name);
            }

            return new(HeimdallAuthOutcome.Unavailable);
        }
    }

    // The first error of Heimdall's envelope, or null when the body is not one (a proxy in front of
    // Heimdall can answer 429 itself, in plain text). Reading it never turns the refusal into another:
    // a body in a charset .NET does not know (InvalidOperationException) counts as no message too.
    private static async Task<string?> ErrorMessageAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<HeimdallErrorEnvelope>(body, ErrorJsonOptions);

            return envelope?.Errors?.FirstOrDefault(error => !string.IsNullOrWhiteSpace(error))?.Trim();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or
            NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    // SocketsHttpHandler wraps what its connect callback throws, so the refusal may be the inner exception.
    private static bool IsPrivateNetworkRefusal(Exception exception) =>
        exception.Message.Contains(PrivateNetworkConnector.RefusedMessage, StringComparison.Ordinal) ||
        exception.InnerException?.Message.Contains(PrivateNetworkConnector.RefusedMessage,
            StringComparison.Ordinal) == true;

    // Only the delta-seconds form is relayed; an HTTP date, or anything that does not parse, is dropped.
    private static int? RetryAfterSeconds(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta && delta >= TimeSpan.Zero &&
        delta.TotalSeconds <= int.MaxValue
            ? (int)delta.TotalSeconds
            : null;

    private sealed class HeimdallEnvelope<T>
    {
        public T? Data { get; init; }
    }

    private sealed class HeimdallErrorEnvelope
    {
        public IReadOnlyList<string?>? Errors { get; init; }
    }
}
