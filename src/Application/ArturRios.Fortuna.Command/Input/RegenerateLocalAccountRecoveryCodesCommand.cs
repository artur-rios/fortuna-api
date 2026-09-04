using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RegenerateLocalAccountRecoveryCodesCommand : BaseCommand
{
    public string Secret { get; set; } = string.Empty;
}
