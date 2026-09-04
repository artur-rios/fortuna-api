using System.Linq.Expressions;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Cards;

namespace ArturRios.Fortuna.Query.Handlers;

internal static class CreditCardStatementProjection
{
    public static readonly Expression<
        Func<CreditCardStatementReadSnapshot, CreditCardStatementOutput>> Expression =
        statement => new CreditCardStatementOutput
        {
            Id = statement.Id,
            CreditCardId = statement.CreditCardId,
            CurrencyCode = statement.CurrencyCode,
            PeriodStart = statement.PeriodStart,
            PeriodEnd = statement.PeriodEnd,
            ClosingDate = statement.ClosingDate,
            DueDate = statement.DueDate,
            PreviousBalance = statement.PreviousBalance,
            PaymentsReceived = statement.PaymentsReceived,
            PurchaseTotal = statement.PurchaseTotal,
            ForeignTaxTotal = statement.ForeignTaxTotal,
            OtherEntries = statement.OtherEntries,
            AmountDue = statement.AmountDue,
            Status = statement.Status == CreditCardStatementStatus.Open
                ? "Open"
                : statement.Status == CreditCardStatementStatus.Closed
                    ? "Closed"
                    : "Settled",
            SettlementTransactionId = statement.SettlementTransactionId,
            CreatedAt = statement.CreatedAt,
            UpdatedAt = statement.UpdatedAt,
            Transactions = statement.Transactions.Select(transaction =>
                new CreditCardStatementTransactionOutput
                {
                    Id = transaction.Id,
                    Direction = transaction.Direction == TransactionDirection.Expense
                        ? "Expense"
                        : "Earning",
                    Amount = transaction.Amount,
                    OccurredOn = transaction.OccurredOn,
                    IsLateArriving = transaction.IsLateArriving,
                    OriginalAmount = transaction.OriginalAmount,
                    OriginalCurrencyCode = transaction.OriginalCurrencyCode,
                    AppliedRate = transaction.AppliedRate,
                    RateDate = transaction.RateDate,
                    CreatedAt = transaction.CreatedAt,
                    UpdatedAt = transaction.UpdatedAt
                }).ToList()
        };

    private static readonly Func<CreditCardStatementReadSnapshot, CreditCardStatementOutput>
        Project = Expression.Compile();

    public static CreditCardStatementOutput From(CreditCardStatementReadSnapshot statement) =>
        Project(statement);
}
