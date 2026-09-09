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

    Task<HeimdallAuthResult<object>> GoogleSignOutAsync(
        string bearerToken,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<object>> RequestPasswordRecoveryAsync(
        string email,
        Guid scopeId,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<object>> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken);

    Task<HeimdallAuthResult<object>> VerifyEmailAsync(
        string token,
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
    NotFound = 5
}

public sealed record HeimdallAuthResult<T>(HeimdallAuthOutcome Outcome, T? Data = default);

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
