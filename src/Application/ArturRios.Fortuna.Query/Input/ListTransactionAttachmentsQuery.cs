using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListTransactionAttachmentsQuery : BaseQuery
{
    public Guid TransactionId { get; set; }
    public bool IncludeDeleted { get; set; }
}
