using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RecoverLocalAccountCommandOutput : CommandOutput
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public int RemainingRecoveryCodes { get; set; }
}
