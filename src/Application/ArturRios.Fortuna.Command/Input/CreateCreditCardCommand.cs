using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class CreateCreditCardCommand : BaseCommand
{
    public string Name { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal CreditLimit { get; set; }
    public short ClosingDay { get; set; }
    public short DueDay { get; set; }
    public string? LastFourDigits { get; set; }
}
