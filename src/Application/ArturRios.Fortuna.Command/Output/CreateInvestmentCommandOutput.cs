using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class CreateInvestmentCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public InvestmentType InvestmentType { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
