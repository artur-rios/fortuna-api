using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class FinancialAccountBalanceOutput : QueryOutput
{
    public Guid Id { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public DateOnly AsOf { get; set; }
}
