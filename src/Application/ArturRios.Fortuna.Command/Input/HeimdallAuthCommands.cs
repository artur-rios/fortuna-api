using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class LoginThroughApiCommand : BaseCommand
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class GoogleSignInThroughApiCommand : BaseCommand
{
    public string IdToken { get; set; } = string.Empty;
}

public sealed class VerifyTwoFactorThroughApiCommand : BaseCommand
{
    public string ChallengeToken { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? RecoveryCode { get; set; }
}

/// <summary>
///     Intent to have Heimdall reissue the email code for a two-factor challenge that is still
///     outstanding (UC-77), so a caller who never received the first one can ask for another
///     without restarting sign-in. Carries only the challenge token — the address the code goes to
///     is resolved by Heimdall from the token itself and is never the caller's to choose.
/// </summary>
public sealed class ResendTwoFactorChallengeCodeThroughApiCommand : BaseCommand
{
    public string ChallengeToken { get; set; } = string.Empty;
}

public sealed class GoogleSignOutThroughApiCommand : BaseCommand
{
    [JsonIgnore]
    public string BearerToken { get; set; } = string.Empty;
}

public sealed class RequestPasswordRecoveryThroughApiCommand : BaseCommand
{
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordThroughApiCommand : BaseCommand
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class VerifyEmailThroughApiCommand : BaseCommand
{
    public string Token { get; set; } = string.Empty;
}

public abstract class AuthenticatedHeimdallCommand : BaseCommand
{
    [JsonIgnore]
    public string BearerToken { get; set; } = string.Empty;
}

public sealed class ResendVerificationThroughApiCommand : AuthenticatedHeimdallCommand;

public sealed class GetTwoFactorStatusThroughApiCommand : AuthenticatedHeimdallCommand;

public sealed class EnableTwoFactorThroughApiCommand : AuthenticatedHeimdallCommand
{
    public List<string> Methods { get; set; } = [];
}

public sealed class ConfirmTwoFactorThroughApiCommand : AuthenticatedHeimdallCommand
{
    public string? AppCode { get; set; }
    public string? EmailCode { get; set; }
}

public sealed class DisableTwoFactorThroughApiCommand : AuthenticatedHeimdallCommand
{
    public string Password { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? RecoveryCode { get; set; }
}

public sealed class RegenerateRecoveryCodesThroughApiCommand : AuthenticatedHeimdallCommand
{
    public string? Code { get; set; }
    public string? RecoveryCode { get; set; }
}
