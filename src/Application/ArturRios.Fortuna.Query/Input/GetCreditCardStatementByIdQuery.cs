using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetCreditCardStatementByIdQuery : BaseQuery
{
    public Guid Id { get; set; }
}
