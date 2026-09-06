using System.Text.Json.Serialization;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListBudgetsQuery : BaseQuery
{
    public bool IncludeDeleted { get; set; }
}

public sealed class GetBudgetByIdQuery : BaseQuery
{
    public Guid Id { get; set; }
    public bool IncludeDeleted { get; set; }
}

public sealed class GetBudgetConsumptionQuery : BaseQuery
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public DateOnly? PeriodStart { get; set; }
}
