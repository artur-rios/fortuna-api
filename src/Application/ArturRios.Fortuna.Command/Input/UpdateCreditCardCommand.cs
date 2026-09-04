using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class UpdateCreditCardCommand : BaseCommand
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public decimal CreditLimit { get; set; }
    public short ClosingDay { get; set; }
    public short DueDay { get; set; }
    public string? CurrencyCode { get; set; }
}
