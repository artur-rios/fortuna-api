namespace ArturRios.Fortuna.Shared.Cards;

public interface ICreditCardStatementSettlementStore
{
    Task<CreditCardStatementSettlementResult> SettleAsync(
        CreditCardStatementSettlement settlement,
        CancellationToken cancellationToken);
}

public sealed record CreditCardStatementSettlement(
    Guid UserId,
    Guid StatementId,
    Guid FinancialAccountId,
    decimal Amount,
    DateOnly PaymentDate,
    DateTimeOffset CreatedAt);

public enum CreditCardStatementSettlementOutcome
{
    Succeeded = 1,
    StatementNotFound = 2,
    FinancialAccountNotFound = 3,
    StatementOpen = 4,
    StatementAlreadySettled = 5,
    ExchangeRateUnavailable = 6
}

public sealed record CreditCardStatementSettlementResult(
    CreditCardStatementSettlementSnapshot? Settlement,
    CreditCardStatementSettlementOutcome Outcome);

public sealed record CreditCardStatementSettlementSnapshot(
    Guid StatementId,
    string Status,
    Guid TransferId,
    Guid OutboundTransactionId,
    Guid InboundTransactionId,
    Guid FinancialAccountId,
    decimal PaymentAmount,
    string PaymentCurrencyCode,
    decimal AppliedAmount,
    string CreditCardCurrencyCode,
    decimal StatementAmountDue,
    decimal RemainingBalance,
    Guid? CarryStatementId,
    decimal CreditAmount,
    decimal? AppliedRate,
    DateOnly? RateDate,
    DateOnly PaymentDate);
