using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class AuthenticateLocalAccountCommand : BaseCommand
{
    public string Name { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}
