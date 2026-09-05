using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Transactions;

namespace ArturRios.Fortuna.Query.Handlers;

internal static class TransactionProjection
{
    public static TransactionOutput Project(TransactionReadSnapshot transaction) => new()
    {
        Id = transaction.Id,
        FinancialAccountId = transaction.FinancialAccountId,
        FinancialAccountName = transaction.FinancialAccountName,
        CreditCardId = transaction.CreditCardId,
        CreditCardName = transaction.CreditCardName,
        CategoryId = transaction.CategoryId,
        CategoryName = transaction.CategoryName,
        CounterpartyId = transaction.CounterpartyId,
        CounterpartyName = transaction.CounterpartyName,
        Direction = transaction.Direction,
        Amount = transaction.Amount,
        CurrencyCode = transaction.CurrencyCode,
        OriginalAmount = transaction.OriginalAmount,
        OriginalCurrencyCode = transaction.OriginalCurrencyCode,
        AppliedRate = transaction.AppliedRate,
        RateDate = transaction.RateDate,
        OccurredOn = transaction.OccurredOn,
        Description = transaction.Description,
        SourceType = transaction.SourceType,
        IsReconciled = transaction.IsReconciled,
        IsManuallyCorrected = transaction.IsManuallyCorrected,
        IsTransfer = transaction.IsTransfer,
        StatementId = transaction.StatementId,
        IsLateArriving = transaction.IsLateArriving,
        Tags = transaction.Tags.Select(tag => new TransactionLabelOutput
        {
            Id = tag.Id,
            Name = tag.Name
        }).ToArray(),
        IsDeleted = transaction.IsDeleted,
        CreatedAt = transaction.CreatedAt,
        UpdatedAt = transaction.UpdatedAt
    };
}
