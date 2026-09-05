using System.Text.Json.Serialization;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class UpdateInvestmentCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string Instrument { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public InvestmentType InvestmentType { get; set; }
    public string? CurrencyCode { get; set; }
}
