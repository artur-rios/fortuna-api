using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListGoalsQuery : BaseQuery
{
    public bool IncludeDeleted { get; set; }
}

public sealed class GetGoalByIdQuery : BaseQuery
{
    public Guid Id { get; set; }
    public bool IncludeDeleted { get; set; }
}
