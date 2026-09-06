using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class GetImportJobByIdQuery : BaseQuery
{
    public Guid Id { get; set; }
}

public sealed class ListImportJobsQuery : BaseQuery
{
    public TransactionSourceType? SourceType { get; set; }
    public ImportJobStatus? Status { get; set; }
    public string SortBy { get; set; } = "CreatedAt";
    public bool Descending { get; set; } = true;
}

public sealed class ListImportedRecordsQuery : BaseQuery
{
    public Guid ImportJobId { get; set; }
}
