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

public sealed class GoogleSignOutThroughApiCommand : BaseCommand
{
    [JsonIgnore]
    public string BearerToken { get; set; } = string.Empty;
}
