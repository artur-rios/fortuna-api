namespace ArturRios.Fortuna.Shared.Security;

public interface IHeimdallAuthGateway
{
    Task<HeimdallAuthResult<HeimdallLoginResult>> LoginAsync(
        string email,
        string password,
        Guid scopeId,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<HeimdallGoogleSignInResult>> GoogleSignInAsync(
        string idToken,
        Guid scopeId,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<HeimdallTwoFactorVerificationResult>> VerifyTwoFactorAsync(
        string challengeToken,
        string? code,
        string? recoveryCode,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<object>> ResendTwoFactorChallengeCodeAsync(
        string challengeToken,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<object>> GoogleSignOutAsync(
        string bearerToken,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<object>> RequestPasswordRecoveryAsync(
        string email,
        Guid scopeId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Asks Heimdall to register a password user in <paramref name="scopeId" /> (UC-81). Succeeds
    ///     identically for a free and a taken address; <see cref="HeimdallAuthOutcome.Closed" /> when the
    ///     scope does not accept registrations.
    /// </summary>
    Task<HeimdallAuthResult<object>> RegisterAsync(
        string name,
        string email,
        string password,
        Guid scopeId,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<object>> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Confirms an emailed token. <paramref name="password" /> is the one chosen at sign-up and is read
    ///     only for a sign-up confirmation link; Heimdall ignores it for a plain email-verification token.
    ///     <see cref="HeimdallAuthOutcome.PasswordRequired" /> when a pending registration needs it,
    ///     <see cref="HeimdallAuthOutcome.Rejected" /> when it is wrong.
    /// </summary>
    Task<HeimdallAuthResult<object>> VerifyEmailAsync(
        string token,
        string? password,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<object>> ResendVerificationAsync(
        string bearerToken,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<HeimdallTwoFactorStatusResult>> GetTwoFactorStatusAsync(
        string bearerToken,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<HeimdallTwoFactorSetupResult>> EnableTwoFactorAsync(
        IReadOnlyCollection<string> methods,
        string bearerToken,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<HeimdallRecoveryCodesResult>> ConfirmTwoFactorAsync(
        string? appCode,
        string? emailCode,
        string bearerToken,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<HeimdallTwoFactorDisabledResult>> DisableTwoFactorAsync(
        string password,
        string? code,
        string? recoveryCode,
        string bearerToken,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<HeimdallRecoveryCodesResult>> RegenerateRecoveryCodesAsync(
        string? code,
        string? recoveryCode,
        string bearerToken,
        CancellationToken cancellationToken);
}

public enum HeimdallAuthOutcome
{
    Succeeded = 1,
    InvalidRequest = 2,
    Rejected = 3,
    Unavailable = 4,
    NotFound = 5,

    /// <summary>The scope does not accept self-registration. Only <c>RegisterAsync</c> produces it.</summary>
    Closed = 6,

    /// <summary>
    ///     Heimdall needs the sign-up password to confirm a pending registration; only
    ///     <c>VerifyEmailAsync</c> produces it.
    /// </summary>
    PasswordRequired = 7,

    /// <summary>
    ///     Heimdall refused the request for its rate (HTTP 429): its anonymous-auth limiter, or a
    ///     per-person mail budget. Every route maps it, with Heimdall's
    ///     <see cref="HeimdallAuthResult{T}.Message" /> and <see cref="HeimdallAuthResult{T}.RetryAfterSeconds" />.
    /// </summary>
    RateLimited = 8,

    /// <summary>
    ///     Heimdall refused the request because of the state it is in (HTTP 409), for example
    ///     two-factor authentication that is already active. Every route maps it, with Heimdall's
    ///     <see cref="HeimdallAuthResult{T}.Message" />.
    /// </summary>
    Conflict = 9
}

/// <summary>The parts of a <see cref="HeimdallAuthResult{T}" /> that do not depend on its data.</summary>
public interface IHeimdallAuthResult
{
    HeimdallAuthOutcome Outcome { get; }

    string? Message { get; }

    int? RetryAfterSeconds { get; }
}

public sealed record HeimdallAuthResult<T>(HeimdallAuthOutcome Outcome, T? Data = default) : IHeimdallAuthResult
{
    /// <summary>
    ///     Heimdall's own message, carried only for <see cref="HeimdallAuthOutcome.RateLimited" /> and
    ///     <see cref="HeimdallAuthOutcome.Conflict" />, whose answers are passed on as Heimdall gave them.
    ///     <c>null</c> when Heimdall's body named none.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    ///     The seconds Heimdall asked the caller to wait (its <c>Retry-After</c> delta), carried only for
    ///     <see cref="HeimdallAuthOutcome.RateLimited" />. <c>null</c> when the header was absent, a
    ///     date, or not a valid number of seconds.
    /// </summary>
    public int? RetryAfterSeconds { get; init; }
}

public sealed record HeimdallLoginResult(
    string? Token,
    DateTimeOffset? ExpiresAt,
    bool? EmailVerified,
    bool RequiresTwoFactor,
    string? ChallengeToken,
    IReadOnlyCollection<string>? AvailableMethods);

public sealed record HeimdallGoogleSignInResult(
    string Token,
    DateTimeOffset ExpiresAt,
    bool EmailVerified);

public sealed record HeimdallTwoFactorVerificationResult(
    string Token,
    DateTimeOffset ExpiresAt,
    bool EmailVerified);

public sealed record HeimdallTwoFactorStatusResult(
    bool IsActive,
    bool AppEnabled,
    bool EmailEnabled,
    int RemainingRecoveryCodes);

public sealed record HeimdallTwoFactorSetupResult(
    string? OtpAuthUri,
    bool? EmailCodeSent);

public sealed record HeimdallRecoveryCodesResult(
    bool? Enabled,
    IReadOnlyCollection<string> RecoveryCodes);

public sealed record HeimdallTwoFactorDisabledResult(bool Disabled);
