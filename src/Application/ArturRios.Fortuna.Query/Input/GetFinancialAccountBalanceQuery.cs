using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetFinancialAccountBalanceQuery : BaseQuery
{
    public Guid Id { get; set; }
    public DateOnly? AsOf { get; set; }
}
