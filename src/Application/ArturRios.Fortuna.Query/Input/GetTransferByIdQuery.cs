using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetTransferByIdQuery : BaseQuery
{
    public Guid Id { get; set; }
    public bool IncludeDeleted { get; set; }
}
