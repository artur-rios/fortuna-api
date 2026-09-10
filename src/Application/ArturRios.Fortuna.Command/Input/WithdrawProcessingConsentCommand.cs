using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class WithdrawProcessingConsentCommand : BaseCommand
{
    public string Purpose { get; set; } = string.Empty;
}
