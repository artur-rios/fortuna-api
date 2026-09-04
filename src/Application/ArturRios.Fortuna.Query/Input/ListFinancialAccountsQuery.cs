using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListFinancialAccountsQuery : BaseQuery
{
    public string? Name { get; set; }
    public string? Institution { get; set; }
    public FinancialAccountType? AccountType { get; set; }
    public string? CurrencyCode { get; set; }
    public bool IncludeDeleted { get; set; }
    public string SortBy { get; set; } = "Name";
    public bool Descending { get; set; }
}
