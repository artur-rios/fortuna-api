using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListInvestmentsQuery : BaseQuery
{
    public string? Instrument { get; set; }
    public string? Institution { get; set; }
    public InvestmentType? InvestmentType { get; set; }
    public string? CurrencyCode { get; set; }
    public string? DisplayCurrencyCode { get; set; }
    public DateOnly? FigureDate { get; set; }
    public string SortBy { get; set; } = "Instrument";
    public bool Descending { get; set; }
}
