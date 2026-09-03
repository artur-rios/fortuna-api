using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RecoverLocalAccountCommand : BaseCommand
{
    public string Name { get; set; } = string.Empty;
    public string RecoveryCode { get; set; } = string.Empty;
    public string NewSecret { get; set; } = string.Empty;
}
