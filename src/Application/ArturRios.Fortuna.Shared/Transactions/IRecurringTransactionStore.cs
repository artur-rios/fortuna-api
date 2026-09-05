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
