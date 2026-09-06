using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListTagsQuery : BaseQuery
{
    public bool IncludeDeleted { get; set; }
}
