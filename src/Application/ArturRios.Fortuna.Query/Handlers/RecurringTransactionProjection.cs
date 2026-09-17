using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Transactions;

namespace ArturRios.Fortuna.Query.Handlers;

internal static class RecurringTransactionProjection
{
    public static RecurringTransactionOutput Project(RecurringTransactionSnapshot rule) => new()
    {
        Id = rule.Id,
        FinancialAccountId = rule.FinancialAccountId,
        CreditCardId = rule.CreditCardId,
        CategoryId = rule.CategoryId,
        Direction = rule.Direction,
        Amount = rule.Amount,
        CurrencyCode = rule.CurrencyCode,
        Frequency = rule.Frequency,
        StartsOn = rule.StartsOn,
        EndsOn = rule.EndsOn,
        LastMaterializedOn = rule.LastMaterializedOn,
        Description = rule.Description,
        CounterpartyId = rule.CounterpartyId,
        CounterpartyName = rule.CounterpartyName,
        NextOccurrences = rule.NextOccurrences,
        IsDeleted = rule.IsDeleted,
        CreatedAt = rule.CreatedAt,
        UpdatedAt = rule.UpdatedAt
    };
}
