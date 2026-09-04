using System.Text.Json.Serialization;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class UpdateFinancialAccountCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public FinancialAccountType AccountType { get; set; }
    public Guid? OwnerId { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? OpeningBalance { get; set; }
}
