using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Shared.Transactions;

public interface IRecurringTransactionStore
{
    Task<RecurringTransactionRecordResult> RecordAsync(
        RecurringTransactionRecord record,
        CancellationToken cancellationToken);
}

public interface IRecurringTransactionReader
{
    Task<RecurringTransactionSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);
}

public interface IRecurringTransactionUpdater
{
    Task<RecurringTransactionUpdateResult> UpdateAsync(
        RecurringTransactionUpdate update,
        CancellationToken cancellationToken);
}

public interface IRecurringTransactionLifecycleStore
{
    Task<RecurringTransactionLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);
}

public interface IRecurringTransactionMaterializer
{
    Task<RecurringMaterializationResult> MaterializeAsync(
        RecurringMaterializationRun run,
        CancellationToken cancellationToken);
}

public sealed record RecurringTransactionRecord(
    Guid UserId,
    Guid? FinancialAccountId,
    Guid? CreditCardId,
    Guid CategoryId,
    TransactionDirection Direction,
    decimal Amount,
    RecurrenceFrequency Frequency,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    string? Description,
    string? Counterparty,
    DateOnly PreviewFrom,
    DateTimeOffset CreatedAt);

public enum RecurringTransactionRecordOutcome
{
    Succeeded = 1,
    FinancialAccountNotFound = 2,
    CreditCardNotFound = 3,
    CategoryNotFound = 4
}

public sealed record RecurringTransactionRecordResult(
    RecurringTransactionSnapshot? Rule,
    RecurringTransactionRecordOutcome Outcome);

public sealed record RecurringTransactionUpdate(
    Guid UserId,
    Guid Id,
    Guid? FinancialAccountId,
    Guid? CreditCardId,
    Guid CategoryId,
    TransactionDirection Direction,
    decimal Amount,
    RecurrenceFrequency Frequency,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    string? Description,
    string? Counterparty,
    DateOnly PreviewFrom,
    DateTimeOffset UpdatedAt);

public enum RecurringTransactionUpdateOutcome
{
    Succeeded = 1,
    NotFound = 2,
    FinancialAccountNotFound = 3,
    CreditCardNotFound = 4,
    CategoryNotFound = 5
}

public sealed record RecurringTransactionUpdateResult(
    RecurringTransactionSnapshot? Rule,
    RecurringTransactionUpdateOutcome Outcome);

public enum RecurringTransactionLifecycleOutcome
{
    Succeeded = 1,
    NotFound = 2
}

public sealed record RecurringTransactionLifecycleResult(
    Guid? Id,
    RecurringTransactionLifecycleOutcome Outcome);

public sealed record RecurringMaterializationRun(
    Guid UserId,
    DateOnly Through,
    DateTimeOffset MaterializedAt);

public sealed record RecurringMaterializationResult(
    IReadOnlyCollection<RecurringRuleMaterializationResult> Rules)
{
    public int CreatedCount => Rules.Sum(rule => rule.CreatedCount);
    public int PossibleDuplicateCount => Rules.Sum(rule => rule.PossibleDuplicateCount);
}

public sealed record RecurringRuleMaterializationResult(
    Guid RuleId,
    IReadOnlyCollection<RecurringOccurrenceMaterializationResult> Occurrences,
    bool IsComplete,
    RecurringMaterializationSkipReason? SkipReason = null)
{
    public int CreatedCount => Occurrences.Count(occurrence => occurrence.TransactionId.HasValue);
    public int PossibleDuplicateCount => Occurrences.Count(occurrence => occurrence.IsPossibleDuplicate);
}

public sealed record RecurringOccurrenceMaterializationResult(
    DateOnly OccurredOn,
    Guid? TransactionId,
    bool IsPossibleDuplicate,
    string? Error = null);

public enum RecurringMaterializationSkipReason
{
    FinancialAccountDeleted = 1,
    CreditCardDeleted = 2,
    CategoryDeleted = 3
}

public static class RecurringMaterializationJob
{
    public const string Type = "recurring-transaction-materialization";
}

public sealed record RecurringMaterializationJobPayload(
    Guid UserId,
    DateOnly Through,
    DateTimeOffset RequestedAt);

public sealed class RecurringTransactionSnapshot
{
    public Guid Id { get; init; }
    public Guid? FinancialAccountId { get; init; }
    public Guid? CreditCardId { get; init; }
    public Guid CategoryId { get; init; }
    public TransactionDirection Direction { get; init; }
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public RecurrenceFrequency Frequency { get; init; }
    public DateOnly StartsOn { get; init; }
    public DateOnly? EndsOn { get; init; }
    public DateOnly? LastMaterializedOn { get; init; }
    public string? Description { get; init; }
    public Guid? CounterpartyId { get; init; }
    public string? CounterpartyName { get; init; }
    public IReadOnlyCollection<DateOnly> NextOccurrences { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
