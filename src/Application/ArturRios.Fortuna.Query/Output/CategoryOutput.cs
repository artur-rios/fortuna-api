using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class CategoryOutput : QueryOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public bool IsDeleted { get; set; }
    public int? UsageCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public IReadOnlyCollection<CategoryOutput> Children { get; set; } = [];
}

public sealed class CategoryTreeOutput : QueryOutput
{
    public IReadOnlyCollection<CategoryOutput> Categories { get; set; } = [];
    public bool CanSeedDefaults { get; set; }
}
