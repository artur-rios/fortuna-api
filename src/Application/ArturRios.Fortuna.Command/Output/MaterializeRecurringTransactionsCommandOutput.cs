using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class MaterializeRecurringTransactionsCommandOutput : CommandOutput
{
    public DateOnly MaterializedThrough { get; set; }
    public int CreatedCount { get; set; }
    public int PossibleDuplicateCount { get; set; }
    public IReadOnlyCollection<RecurringRuleMaterializationCommandOutput> Rules { get; set; } = [];
}

public sealed class RecurringRuleMaterializationCommandOutput
{
    public Guid RuleId { get; set; }
    public int CreatedCount { get; set; }
    public int PossibleDuplicateCount { get; set; }
    public bool IsComplete { get; set; }
    public string? SkipReason { get; set; }
    public IReadOnlyCollection<RecurringOccurrenceMaterializationCommandOutput> Occurrences { get; set; } = [];
}

public sealed class RecurringOccurrenceMaterializationCommandOutput
{
    public DateOnly OccurredOn { get; set; }
    public Guid? TransactionId { get; set; }
    public bool IsPossibleDuplicate { get; set; }
    public string? Error { get; set; }
}
