using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListCreditCardsQuery : BaseQuery
{
    public string? Name { get; set; }
    public string? Issuer { get; set; }
    public string? CurrencyCode { get; set; }
    public string SortBy { get; set; } = "Name";
    public bool Descending { get; set; }
}
