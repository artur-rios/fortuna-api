using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class CurrencyOutput : QueryOutput
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public short MinorUnitDigits { get; set; }
}
