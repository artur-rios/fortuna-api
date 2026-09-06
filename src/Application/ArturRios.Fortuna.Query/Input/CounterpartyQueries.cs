using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListCounterpartiesQuery : BaseQuery
{
    public bool IncludeDeleted { get; set; }
}

public sealed class SuggestCounterpartyCategoryQuery : BaseQuery
{
    public Guid Id { get; set; }
}
