using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class TransactionDrillDownOutput : QueryOutput
{
    public string Mode { get; set; } = string.Empty;
    public string SourceDimension { get; set; } = string.Empty;
    public string? Dimension { get; set; }
    public bool MayDifferFromChart { get; set; }
    public IReadOnlyCollection<TransactionAggregationBucketOutput> Buckets { get; set; } = [];
    public TransactionOutput? Transaction { get; set; }
    public IReadOnlyCollection<TransactionOutput> Transactions { get; set; } = [];
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => PageSize == 0
        ? 0
        : (int)Math.Ceiling((decimal)TotalItems / PageSize);
}
