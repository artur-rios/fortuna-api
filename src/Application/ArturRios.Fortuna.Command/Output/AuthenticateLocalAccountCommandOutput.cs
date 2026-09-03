using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class AuthenticateLocalAccountCommandOutput : CommandOutput
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
