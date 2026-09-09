using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class LoginThroughApiCommandOutput : CommandOutput
{
    public string? Token { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool? EmailVerified { get; set; }
    public bool RequiresTwoFactor { get; set; }
    public string? ChallengeToken { get; set; }
    public IReadOnlyCollection<string>? AvailableMethods { get; set; }
}

public sealed class GoogleSignInThroughApiCommandOutput : CommandOutput
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool EmailVerified { get; set; }
}

public sealed class VerifyTwoFactorThroughApiCommandOutput : CommandOutput
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool EmailVerified { get; set; }
}

public sealed class GoogleSignOutThroughApiCommandOutput : CommandOutput;
