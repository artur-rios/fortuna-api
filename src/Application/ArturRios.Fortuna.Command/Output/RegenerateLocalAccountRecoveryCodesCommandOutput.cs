using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RegenerateLocalAccountRecoveryCodesCommandOutput : CommandOutput
{
    public IReadOnlyCollection<string> RecoveryCodes { get; set; } = [];
    public string RecoveryWarning { get; set; } = string.Empty;
}
