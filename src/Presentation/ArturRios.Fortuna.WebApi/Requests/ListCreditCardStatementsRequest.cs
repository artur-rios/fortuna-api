using ArturRios.Fortuna.Domain.Cards;

namespace ArturRios.Fortuna.WebApi.Requests;

public sealed class ListCreditCardStatementsRequest
{
    public CreditCardStatementStatus? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string SortBy { get; set; } = "PeriodStart";
    public bool Descending { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
