using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetNetPositionQuery : BaseQuery
{
    public string? DisplayCurrencyCode { get; set; }
    public DateOnly? AsOf { get; set; }
}
