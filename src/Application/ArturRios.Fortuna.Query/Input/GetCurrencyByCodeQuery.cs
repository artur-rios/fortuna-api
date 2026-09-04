using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetCurrencyByCodeQuery : BaseQuery
{
    public string Code { get; set; } = string.Empty;
}
