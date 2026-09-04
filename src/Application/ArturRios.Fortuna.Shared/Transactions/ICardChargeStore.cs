namespace ArturRios.Fortuna.Shared.Transactions;

public interface ICardChargeStore
{
    Task<CardChargeCreationResult> CreateAsync(
        CardChargeCreation creation,
        CancellationToken cancellationToken);
}

public sealed record CardChargeCreation(
    Guid UserId,
    Guid CreditCardId,
    decimal Amount,
    DateOnly OccurredOn,
    DateTimeOffset CreatedAt);

public sealed record CardChargeCreationResult(
    CardChargeSnapshot? Charge,
    bool CardNotFound);

public sealed record CardChargeSnapshot(
    Guid Id,
    Guid CreditCardId,
    decimal Amount,
    DateOnly OccurredOn,
    bool IsLateArriving,
    Guid StatementId,
    DateOnly StatementPeriodStart,
    DateOnly StatementPeriodEnd,
    DateOnly StatementClosingDate,
    DateOnly StatementDueDate,
    string StatementStatus,
    decimal StatementPurchaseTotal);
