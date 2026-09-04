using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Cards;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Cards;

public sealed class EfCreditCardStatementStore(AppDbContext context)
    : ICreditCardStatementCloser, ICreditCardStatementReader
{
    public IQueryable<CreditCardStatementReadSnapshot> Query(Guid userId)
    {
        var statements = context.CreditCardStatements
            .AsNoTracking()
            .Where(statement =>
                statement.CreditCard.User.PublicId == userId &&
                !statement.IsDeleted &&
                !statement.CreditCard.IsDeleted)
            .Select(statement => new
            {
                Statement = statement,
                PurchaseTotal = context.FinancialTransactions
                    .Where(transaction =>
                        transaction.StatementId == statement.Id &&
                        !transaction.IsDeleted)
                    .Select(transaction => (decimal?)(
                        transaction.Direction == TransactionDirection.Expense
                            ? transaction.Amount
                            : -transaction.Amount))
                    .Sum() ?? 0m
            });

        return statements.Select(item => new CreditCardStatementReadSnapshot
        {
            Id = item.Statement.PublicId,
            CreditCardId = item.Statement.CreditCard.PublicId,
            CurrencyCode = item.Statement.CreditCard.Currency.Code,
            PeriodStart = item.Statement.PeriodStart,
            PeriodEnd = item.Statement.PeriodEnd,
            ClosingDate = item.Statement.ClosingDate,
            DueDate = item.Statement.DueDate,
            PreviousBalance = item.Statement.PreviousBalance,
            PaymentsReceived = item.Statement.PaymentsReceived,
            PurchaseTotal = item.PurchaseTotal,
            ForeignTaxTotal = item.Statement.ForeignTaxTotal,
            OtherEntries = item.Statement.OtherEntries,
            AmountDue = item.Statement.PreviousBalance - item.Statement.PaymentsReceived +
                item.PurchaseTotal + item.Statement.ForeignTaxTotal + item.Statement.OtherEntries,
            Status = item.Statement.Status,
            SettlementTransactionId = item.Statement.SettlementTransaction == null
                ? null
                : item.Statement.SettlementTransaction.PublicId,
            IsDeleted = item.Statement.IsDeleted,
            CreatedAt = item.Statement.CreatedAt,
            UpdatedAt = item.Statement.UpdatedAt,
            Transactions = context.FinancialTransactions
                .Where(transaction =>
                    transaction.StatementId == item.Statement.Id &&
                    !transaction.IsDeleted)
                .OrderBy(transaction => transaction.OccurredOn)
                .ThenBy(transaction => transaction.CreatedAt)
                .ThenBy(transaction => transaction.Id)
                .Select(transaction => new CreditCardStatementTransactionSnapshot
                {
                    Id = transaction.PublicId,
                    Direction = transaction.Direction,
                    Amount = transaction.Amount,
                    OccurredOn = transaction.OccurredOn,
                    IsLateArriving = transaction.IsLateArriving,
                    OriginalAmount = transaction.OriginalAmount,
                    OriginalCurrencyCode = transaction.OriginalCurrency == null
                        ? null
                        : transaction.OriginalCurrency.Code,
                    AppliedRate = transaction.AppliedRate,
                    RateDate = transaction.RateDate,
                    CreatedAt = transaction.CreatedAt,
                    UpdatedAt = transaction.UpdatedAt
                })
                .ToList()
        });
    }

    public Task<CreditCardStatementReadSnapshot?> FindByIdAsync(
        Guid userId,
        Guid statementId,
        CancellationToken cancellationToken) => Query(userId).SingleOrDefaultAsync(
        statement => statement.Id == statementId,
        cancellationToken);

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
