using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class UpdateCategoryCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}
