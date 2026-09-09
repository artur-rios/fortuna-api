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
}

public enum HeimdallAuthOutcome
{
    Succeeded = 1,
    InvalidRequest = 2,
    Rejected = 3,
    Unavailable = 4
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
