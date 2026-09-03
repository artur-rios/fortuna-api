using ArturRios.Fortuna.Domain.Users;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class CreateLocalAccountCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public LocalAccountStorageMode StorageMode { get; set; }
    public IReadOnlyCollection<string> RecoveryCodes { get; set; } = [];
    public string RecoveryWarning { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
