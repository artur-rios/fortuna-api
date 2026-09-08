using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListCommittedObligationsQuery : BaseQuery
{
    public int HorizonDays { get; set; }
    public string? DisplayCurrencyCode { get; set; }
}
