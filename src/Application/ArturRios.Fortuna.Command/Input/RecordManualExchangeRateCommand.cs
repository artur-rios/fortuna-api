using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RecordManualExchangeRateCommand : BaseCommand
{
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public string QuoteCurrencyCode { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateOnly RateDate { get; set; }
}
