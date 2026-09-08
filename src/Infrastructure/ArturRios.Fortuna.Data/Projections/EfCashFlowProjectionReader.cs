using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Projections;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Projections;

public sealed class EfCashFlowProjectionReader(AppDbContext context) : ICashFlowProjectionReader
{
    public async Task<CashFlowProjectionSnapshot> ReadAsync(
        Guid userId,
        DateOnly asOf,
        DateOnly through,
        DateOnly historyFrom,
        CancellationToken cancellationToken)
    {
        var balances = await context.FinancialAccounts
            .AsNoTracking()
            .Where(account => account.User.PublicId == userId && !account.IsDeleted)
            .Select(account => new CashFlowCurrencyAmountSnapshot(
                account.Currency.Code,
                account.OpeningBalance + (context.FinancialTransactions
                    .Where(transaction =>
                        transaction.FinancialAccountId == account.Id &&
                        !transaction.IsDeleted &&
                        transaction.OccurredOn <= asOf)
                    .Select(transaction => (decimal?)(transaction.Direction ==
                        TransactionDirection.Earning
                            ? transaction.Amount
                            : -transaction.Amount))
                    .Sum() ?? 0m)))
            .ToArrayAsync(cancellationToken);

        var future = new List<CashFlowAmountSnapshot>();
        var firstProjectedDay = asOf.AddDays(1);
        var recurringRules = await context.RecurringTransactions
            .AsNoTracking()
            .Include(rule => rule.Currency)
            .Where(rule =>
                rule.User.PublicId == userId &&
                !rule.IsDeleted &&
                !rule.Category.IsDeleted &&
                (rule.FinancialAccount != null && !rule.FinancialAccount.IsDeleted ||
                    rule.CreditCard != null && !rule.CreditCard.IsDeleted) &&
                rule.StartsOn <= through &&
                (!rule.EndsOn.HasValue || rule.EndsOn >= firstProjectedDay))
            .ToArrayAsync(cancellationToken);
        foreach (var rule in recurringRules)
        {
            var from = rule.LastMaterializedOn.HasValue &&
                rule.LastMaterializedOn.Value >= firstProjectedDay
                    ? rule.LastMaterializedOn.Value.AddDays(1)
                    : firstProjectedDay;
            future.AddRange(rule.OccurrencesBetween(from, through).Select(date =>
                new CashFlowAmountSnapshot(
                    date,
                    rule.Currency.Code,
                    rule.Direction == TransactionDirection.Earning
                        ? rule.Amount
                        : -rule.Amount,
                    CashFlowSourceKind.Recurring)));
        }

        var installments = await context.FinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.User.PublicId == userId &&
                transaction.InstallmentPlanId != null &&
                !transaction.IsDeleted &&
                !transaction.InstallmentPlan!.IsDeleted &&
                !transaction.CreditCard!.IsDeleted &&
                transaction.OccurredOn >= firstProjectedDay &&
                transaction.OccurredOn <= through)
            .Select(transaction => new CashFlowAmountSnapshot(
                transaction.OccurredOn,
                transaction.Currency.Code,
                transaction.Direction == TransactionDirection.Earning
                    ? transaction.Amount
                    : -transaction.Amount,
                CashFlowSourceKind.Installment))
            .ToArrayAsync(cancellationToken);
        future.AddRange(installments);

        var statements = await context.CreditCardStatements
            .AsNoTracking()
            .Where(statement =>
                statement.CreditCard.User.PublicId == userId &&
                statement.Status == CreditCardStatementStatus.Closed &&
                !statement.IsDeleted &&
                !statement.CreditCard.IsDeleted &&
                statement.DueDate <= through &&
                statement.AmountDue != 0m)
            .Select(statement => new
            {
                statement.DueDate,
                CurrencyCode = statement.CreditCard.Currency.Code,
                statement.AmountDue
            })
            .ToArrayAsync(cancellationToken);
        future.AddRange(statements.Select(statement => new CashFlowAmountSnapshot(
            statement.DueDate < firstProjectedDay ? firstProjectedDay : statement.DueDate,
            statement.CurrencyCode,
            -statement.AmountDue,
            CashFlowSourceKind.Statement)));

        var history = await context.FinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.User.PublicId == userId &&
                transaction.FinancialAccountId != null &&
                !transaction.IsDeleted &&
                !transaction.FinancialAccount!.IsDeleted &&
                transaction.OccurredOn >= historyFrom &&
                transaction.OccurredOn <= asOf &&
                !context.Transfers.Any(transfer =>
                    transfer.OutboundTransactionId == transaction.Id ||
                    transfer.InboundTransactionId == transaction.Id))
            .Select(transaction => new CashFlowAmountSnapshot(
                transaction.OccurredOn,
                transaction.Currency.Code,
                transaction.Direction == TransactionDirection.Earning
                    ? transaction.Amount
                    : -transaction.Amount,
                CashFlowSourceKind.Historical))
            .ToArrayAsync(cancellationToken);
        var historyStartsOn = await context.FinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.User.PublicId == userId &&
                transaction.FinancialAccountId != null &&
                !transaction.IsDeleted &&
                !transaction.FinancialAccount!.IsDeleted &&
                transaction.OccurredOn <= asOf &&
                !context.Transfers.Any(transfer =>
                    transfer.OutboundTransactionId == transaction.Id ||
                    transfer.InboundTransactionId == transaction.Id))
            .Select(transaction => (DateOnly?)transaction.OccurredOn)
            .MinAsync(cancellationToken);

        return new CashFlowProjectionSnapshot(
            balances,
            future.OrderBy(item => item.Date).ToArray(),
            history,
            historyStartsOn);
    }
}
