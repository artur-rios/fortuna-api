using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class SearchTransactionsQuery : BaseQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? FinancialAccountId { get; set; }
    public Guid? CreditCardId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? TagId { get; set; }
    public Guid? CounterpartyId { get; set; }
    public TransactionDirection? Direction { get; set; }
    public decimal? MinimumAmount { get; set; }
    public decimal? MaximumAmount { get; set; }
    public string? Text { get; set; }
    public bool IncludeDeleted { get; set; }
    public string? DisplayCurrencyCode { get; set; }
    public DateOnly? FigureDate { get; set; }
    public string SortBy { get; set; } = "OccurredOn";
    public bool Descending { get; set; } = true;
}
