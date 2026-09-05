using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetRecurringTransactionByIdQuery : BaseQuery
{
    public Guid Id { get; set; }
}
