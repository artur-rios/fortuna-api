using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetInvestmentByIdQuery : BaseQuery
{
    public Guid Id { get; set; }
    public string? DisplayCurrencyCode { get; set; }
    public DateOnly? FigureDate { get; set; }
}
