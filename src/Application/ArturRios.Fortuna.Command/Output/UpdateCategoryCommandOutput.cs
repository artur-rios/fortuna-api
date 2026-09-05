using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class UpdateCategoryCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
