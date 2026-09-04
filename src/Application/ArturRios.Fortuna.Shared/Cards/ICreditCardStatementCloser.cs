namespace ArturRios.Fortuna.Shared.Cards;

public interface ICreditCardStatementCloser
{
    Task<CreditCardStatementCloseResult> CloseAsync(
        Guid userId,
        Guid statementId,
        DateOnly asOf,
        bool explicitRequest,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);
}

public enum CreditCardStatementCloseOutcome
{
    Succeeded = 1,
    NotFound = 2,
    NotDue = 3,
    SettledStatementFrozen = 4
}

public sealed record CreditCardStatementCloseResult(
    CreditCardStatementSnapshot? Statement,
    CreditCardStatementCloseOutcome Outcome);

public sealed record CreditCardStatementSnapshot(
    Guid Id,
    Guid CreditCardId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly ClosingDate,
    DateOnly DueDate,
    string Status,
    decimal PurchaseTotal,
    decimal AmountDue);
