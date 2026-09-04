using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetCreditCardByIdQuery : BaseQuery
{
    public Guid Id { get; set; }
}
