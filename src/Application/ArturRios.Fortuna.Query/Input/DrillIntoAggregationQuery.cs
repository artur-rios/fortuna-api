using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class DrillIntoAggregationQuery : BaseQuery
{
    public string Key { get; set; } = string.Empty;
    public string? Dimension { get; set; }
}
