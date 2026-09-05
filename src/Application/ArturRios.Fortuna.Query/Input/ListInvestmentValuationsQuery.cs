using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListInvestmentValuationsQuery : BaseQuery
{
    public Guid InvestmentId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string SortBy { get; set; } = "ValuedOn";
    public bool Descending { get; set; } = true;
}
