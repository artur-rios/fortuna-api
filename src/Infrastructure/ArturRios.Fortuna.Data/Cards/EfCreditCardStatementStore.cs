using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Transactions;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Cards;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Cards;

public sealed class EfCreditCardStatementStore(AppDbContext context)
    : ICreditCardStatementCloser, ICreditCardStatementReader,
        ICreditCardStatementSettlementStore
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

    public async Task<CreditCardStatementSettlementResult> SettleAsync(
        CreditCardStatementSettlement settlement,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var statement = await context.CreditCardStatements
            .Include(item => item.CreditCard)
            .ThenInclude(item => item.User)
            .Include(item => item.CreditCard)
            .ThenInclude(item => item.Currency)
            .SingleOrDefaultAsync(item =>
                item.PublicId == settlement.StatementId &&
                item.CreditCard.User.PublicId == settlement.UserId &&
                !item.IsDeleted &&
                !item.CreditCard.IsDeleted,
                cancellationToken);
        if (statement is null)
        {
            return SettlementResult(CreditCardStatementSettlementOutcome.StatementNotFound);
        }

        if (statement.Status == CreditCardStatementStatus.Open)
        {
            return SettlementResult(CreditCardStatementSettlementOutcome.StatementOpen);
        }

        if (statement.Status == CreditCardStatementStatus.Settled)
        {
            return SettlementResult(
                CreditCardStatementSettlementOutcome.StatementAlreadySettled);
        }

        var account = await context.FinancialAccounts
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleOrDefaultAsync(item =>
                item.PublicId == settlement.FinancialAccountId &&
                item.User.PublicId == settlement.UserId &&
                !item.IsDeleted,
                cancellationToken);
        if (account is null)
        {
            return SettlementResult(
                CreditCardStatementSettlementOutcome.FinancialAccountNotFound);
        }

        ExchangeRate? exchangeRate = null;
        var appliedAmount = settlement.Amount;
        if (account.Currency.Code != statement.CreditCard.Currency.Code)
        {
            exchangeRate = await context.ExchangeRates
                .Include(rate => rate.BaseCurrency)
                .Include(rate => rate.QuoteCurrency)
                .Where(rate =>
                    rate.BaseCurrency.Code == account.Currency.Code &&
                    rate.QuoteCurrency.Code == statement.CreditCard.Currency.Code &&
                    rate.RateDate <= settlement.PaymentDate)
                .OrderByDescending(rate => rate.RateDate)
                .ThenByDescending(rate => rate.Source)
                .FirstOrDefaultAsync(cancellationToken);
            if (exchangeRate is null)
            {
                return SettlementResult(
                    CreditCardStatementSettlementOutcome.ExchangeRateUnavailable);
            }

            appliedAmount = decimal.Round(
                settlement.Amount * exchangeRate.Rate,
                statement.CreditCard.Currency.MinorUnitDigits,
                MidpointRounding.AwayFromZero);
        }

        var transferCategory = await TransactionCategoryResolver.GetOrCreateAsync(
            context,
            account.User,
            TransactionCategoryResolver.Transfers,
            settlement.CreatedAt,
            cancellationToken);
        var outbound = new FinancialTransaction(
            account.User,
            account,
            transferCategory,
            TransactionDirection.Expense,
            settlement.Amount,
            settlement.PaymentDate,
            settlement.CreatedAt);
        var inbound = new FinancialTransaction(
            statement.CreditCard.User,
            statement.CreditCard,
            transferCategory,
            TransactionDirection.Earning,
            appliedAmount,
            settlement.PaymentDate,
            settlement.CreatedAt);
        if (exchangeRate is not null)
        {
            inbound.RecordForeignCurrencyDetails(
                settlement.Amount,
                account.Currency,
                exchangeRate.Rate,
                exchangeRate.RateDate,
                settlement.CreatedAt);
        }

        var transfer = new Transfer(
            outbound,
            inbound,
            exchangeRate?.Rate,
            exchangeRate?.RateDate,
            settlement.CreatedAt);
        var remainingBalance = Math.Max(statement.AmountDue - appliedAmount, 0m);
        var creditAmount = Math.Max(appliedAmount - statement.AmountDue, 0m);
        CreditCardStatement? carryStatement = null;
        if (remainingBalance > 0m)
        {
            var cycle = BillingCycle.Containing(
                statement.PeriodEnd.AddDays(1),
                statement.CreditCard.ClosingDay,
                statement.CreditCard.DueDay);
            while (true)
            {
                carryStatement = await context.CreditCardStatements.SingleOrDefaultAsync(item =>
                    item.CreditCardId == statement.CreditCardId &&
                    item.PeriodStart == cycle.PeriodStart &&
                    item.PeriodEnd == cycle.PeriodEnd &&
                    !item.IsDeleted,
                    cancellationToken);
                if (carryStatement is null)
                {
                    carryStatement = new CreditCardStatement(
                        statement.CreditCard,
                        cycle,
                        settlement.CreatedAt);
                    context.CreditCardStatements.Add(carryStatement);
                    break;
                }

                if (carryStatement.Status != CreditCardStatementStatus.Settled)
                {
                    break;
                }

                cycle = cycle.Next(
                    statement.CreditCard.ClosingDay,
                    statement.CreditCard.DueDay);
            }

            carryStatement.SetPreviousBalance(
                carryStatement.PreviousBalance + remainingBalance,
                settlement.CreatedAt);
        }

        statement.Settle(inbound, settlement.CreatedAt);
        context.FinancialTransactions.AddRange(outbound, inbound);
        context.Transfers.Add(transfer);
        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return SettlementResult(
            CreditCardStatementSettlementOutcome.Succeeded,
            new CreditCardStatementSettlementSnapshot(
                statement.PublicId,
                statement.Status.ToString(),
                transfer.PublicId,
                outbound.PublicId,
                inbound.PublicId,
                account.PublicId,
                settlement.Amount,
                account.Currency.Code,
                appliedAmount,
                statement.CreditCard.Currency.Code,
                statement.AmountDue,
                remainingBalance,
                carryStatement?.PublicId,
                creditAmount,
                exchangeRate?.Rate,
                exchangeRate?.RateDate,
                settlement.PaymentDate));
    }

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

    private static CreditCardStatementSettlementResult SettlementResult(
        CreditCardStatementSettlementOutcome outcome,
        CreditCardStatementSettlementSnapshot? settlement = null) => new(
        settlement,
        outcome);
}
