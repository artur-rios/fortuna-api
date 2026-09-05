using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetCategoryTreeQuery : BaseQuery
{
    public bool IncludeDeleted { get; set; }
    public bool IncludeUsageCounts { get; set; }
}

public sealed class GetCategoryByIdQuery : BaseQuery
{
    public Guid Id { get; set; }
    public bool IncludeDeleted { get; set; }
    public bool IncludeUsageCounts { get; set; }
}
