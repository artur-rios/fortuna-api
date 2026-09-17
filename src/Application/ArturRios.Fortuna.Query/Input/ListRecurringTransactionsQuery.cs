using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListRecurringTransactionsQuery : BaseQuery
{
    public bool? Active { get; set; }
    public bool IncludeDeleted { get; set; }
    public string SortBy { get; set; } = "StartsOn";
    public bool Descending { get; set; }
}
