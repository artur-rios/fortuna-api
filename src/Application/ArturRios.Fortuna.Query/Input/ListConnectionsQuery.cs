using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListConnectionsQuery : BaseQuery
{
    public TransactionSourceType? DataSourceType { get; set; }
    public ConnectionStatus? Status { get; set; }
    public string SortBy { get; set; } = "CreatedAt";
    public bool Descending { get; set; } = true;
}
