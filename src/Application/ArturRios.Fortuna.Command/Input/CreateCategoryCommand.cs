using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CreateCategoryCommand : BaseCommand
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}
