using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class TagOutput : QueryOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TagListOutput : QueryOutput
{
    public IReadOnlyCollection<TagOutput> Tags { get; set; } = [];
}
