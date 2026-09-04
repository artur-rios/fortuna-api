using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class ListSupportedCurrenciesQueryOutput : QueryOutput
{
    public IReadOnlyCollection<CurrencyOutput> Currencies { get; set; } = [];
}
