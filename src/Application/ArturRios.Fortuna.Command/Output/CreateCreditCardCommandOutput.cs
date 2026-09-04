using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class CreateCreditCardCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal CreditLimit { get; set; }
    public short ClosingDay { get; set; }
    public short DueDay { get; set; }
    public string? LastFourDigits { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
