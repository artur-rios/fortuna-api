using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class ImportJobOutput : QueryOutput
{
    public Guid Id { get; set; }
    public Guid? ConnectionId { get; set; }
    public TransactionSourceType SourceType { get; set; }
    public ImportJobStatus Status { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public int ProcessedCount { get; set; }
    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int RejectedCount { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ImportedRecordOutput : QueryOutput
{
    public string RawPayload { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public ImportedRecordOutcome Outcome { get; set; }
    public string? RejectionReason { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly? OccurredOn { get; set; }
}
