using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RecordManualExchangeRateCommandOutput : CommandOutput
{
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public string QuoteCurrencyCode { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateOnly RateDate { get; set; }
    public ExchangeRateSource Source { get; set; }
    public bool TakesPrecedence { get; set; }
    public bool ReplacedExisting { get; set; }
}
