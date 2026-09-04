using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class FinancialAccountOutput : QueryOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public FinancialAccountType AccountType { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
