using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Cards;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Cards;

public sealed class EfCreditCardStatementStore(AppDbContext context) : ICreditCardStatementCloser
{
    public async Task<CreditCardStatementCloseResult> CloseAsync(
        Guid userId,
        Guid statementId,
        DateOnly asOf,
        bool explicitRequest,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var statement = await context.CreditCardStatements
            .Include(item => item.CreditCard)
            .ThenInclude(item => item.User)
            .SingleOrDefaultAsync(item =>
                item.PublicId == statementId &&
                item.CreditCard.User.PublicId == userId &&
                !item.IsDeleted &&
                !item.CreditCard.IsDeleted,
                cancellationToken);
        if (statement is null)
        {
            return new CreditCardStatementCloseResult(
                null,
                CreditCardStatementCloseOutcome.NotFound);
        }

        if (statement.Status == CreditCardStatementStatus.Settled)
        {
            return Result(statement, CreditCardStatementCloseOutcome.SettledStatementFrozen);
        }

        if (statement.Status == CreditCardStatementStatus.Open &&
            !explicitRequest &&
            statement.ClosingDate >= asOf)
        {
            return Result(statement, CreditCardStatementCloseOutcome.NotDue);
        }

        var purchaseTotal = await context.FinancialTransactions
            .Where(item => item.StatementId == statement.Id && !item.IsDeleted)
            .Select(item => (decimal?)(item.Direction == TransactionDirection.Expense
                ? item.Amount
                : -item.Amount))
            .SumAsync(cancellationToken) ?? 0m;
        statement.RecalculatePurchaseTotal(purchaseTotal, changedAt);
        statement.Close(changedAt);
        await context.SaveChangesAsync(cancellationToken);
        return Result(statement, CreditCardStatementCloseOutcome.Succeeded);
    }

    private static CreditCardStatementCloseResult Result(
        CreditCardStatement statement,
        CreditCardStatementCloseOutcome outcome) => new(
        new CreditCardStatementSnapshot(
            statement.PublicId,
            statement.CreditCard.PublicId,
            statement.PeriodStart,
            statement.PeriodEnd,
            statement.ClosingDate,
            statement.DueDate,
            statement.Status.ToString(),
            statement.PurchaseTotal,
            statement.AmountDue),
        outcome);
}
