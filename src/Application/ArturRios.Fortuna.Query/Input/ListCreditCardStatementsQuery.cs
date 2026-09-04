using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListCreditCardStatementsQuery : BaseQuery
{
    public Guid CreditCardId { get; set; }
    public CreditCardStatementStatus? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string SortBy { get; set; } = "PeriodStart";
    public bool Descending { get; set; } = true;
}
