using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CreateInvestmentCommand : BaseCommand
{
    public string Instrument { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public InvestmentType InvestmentType { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}
