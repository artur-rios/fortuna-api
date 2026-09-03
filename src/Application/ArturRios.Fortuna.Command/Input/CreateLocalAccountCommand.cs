using ArturRios.Fortuna.Domain.Users;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CreateLocalAccountCommand : BaseCommand
{
    public string DisplayName { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public LocalAccountStorageMode StorageMode { get; set; }
}
