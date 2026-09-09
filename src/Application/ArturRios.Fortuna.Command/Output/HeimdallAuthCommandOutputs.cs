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

public sealed class RequestPasswordRecoveryThroughApiCommandOutput : CommandOutput;

public sealed class ResetPasswordThroughApiCommandOutput : CommandOutput;

public sealed class VerifyEmailThroughApiCommandOutput : CommandOutput;

public sealed class ResendVerificationThroughApiCommandOutput : CommandOutput;

public sealed class GetTwoFactorStatusThroughApiCommandOutput : CommandOutput
{
    public bool IsActive { get; set; }
    public bool AppEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public int RemainingRecoveryCodes { get; set; }
}

public sealed class EnableTwoFactorThroughApiCommandOutput : CommandOutput
{
    public string? OtpAuthUri { get; set; }
    public bool? EmailCodeSent { get; set; }
}

public sealed class ConfirmTwoFactorThroughApiCommandOutput : CommandOutput
{
    public bool Enabled { get; set; }
    public IReadOnlyCollection<string> RecoveryCodes { get; set; } = [];
}

public sealed class DisableTwoFactorThroughApiCommandOutput : CommandOutput
{
    public bool Disabled { get; set; }
}

public sealed class RegenerateRecoveryCodesThroughApiCommandOutput : CommandOutput
{
    public IReadOnlyCollection<string> RecoveryCodes { get; set; } = [];
}
