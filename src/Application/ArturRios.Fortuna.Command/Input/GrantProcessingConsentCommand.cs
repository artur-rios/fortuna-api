using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class GrantProcessingConsentCommand : BaseCommand
{
    public string Purpose { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
