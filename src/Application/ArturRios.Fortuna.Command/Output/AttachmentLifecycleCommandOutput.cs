using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class AttachmentLifecycleCommandOutput : CommandOutput
{
    public Guid Id { get; init; }
    public bool IsDeleted { get; init; }
}
