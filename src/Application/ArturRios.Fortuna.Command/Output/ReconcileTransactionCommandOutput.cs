using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class ReconcileTransactionCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly OccurredOn { get; set; }
    public bool IsReconciled { get; set; }
    public TransactionReconciliationOutput? Reconciliation { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TransactionReconciliationOutput
{
    public Guid ImportJobId { get; set; }
    public long ImportedRecordId { get; set; }
    public bool HasDiscrepancy { get; set; }
    public decimal TransactionAmount { get; set; }
    public decimal ImportedAmount { get; set; }
    public DateOnly TransactionOccurredOn { get; set; }
    public DateOnly ImportedOccurredOn { get; set; }
}
