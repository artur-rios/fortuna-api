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
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => PageSize == 0
        ? 0
        : (int)Math.Ceiling((decimal)TotalItems / PageSize);
}
