using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CreateFinancialAccountCommand : BaseCommand
{
    public string Name { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public FinancialAccountType AccountType { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
}
