using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Transactions;

public sealed class EfCardChargeStore(AppDbContext context) : ICardChargeStore
{
    public async Task<CardChargeCreationResult> CreateAsync(
        CardChargeCreation creation,
        CancellationToken cancellationToken)
    {
        var card = await context.CreditCards
            .Include(item => item.User)
            .SingleOrDefaultAsync(item =>
                item.User.PublicId == creation.UserId &&
                item.PublicId == creation.CreditCardId &&
                !item.IsDeleted,
                cancellationToken);
        if (card is null)
        {
            return new CardChargeCreationResult(null, CardNotFound: true);
        }

        var statements = await context.CreditCardStatements
            .Where(item => item.CreditCardId == card.Id && !item.IsDeleted)
            .OrderBy(item => item.PeriodStart)
            .ToListAsync(cancellationToken);
        var intendedCycle = BillingCycle.Containing(
            creation.OccurredOn,
            card.ClosingDay,
            card.DueDay);
        var statement = statements.SingleOrDefault(item =>
            item.PeriodStart == intendedCycle.PeriodStart &&
            item.PeriodEnd == intendedCycle.PeriodEnd);
        var isLateArriving = statement?.Status == CreditCardStatementStatus.Settled;

        if (isLateArriving)
        {
            var cycle = intendedCycle.Next(card.ClosingDay, card.DueDay);
            while (true)
            {
                statement = statements.SingleOrDefault(item =>
                    item.PeriodStart == cycle.PeriodStart &&
                    item.PeriodEnd == cycle.PeriodEnd);
                if (statement is null)
                {
                    statement = new CreditCardStatement(card, cycle, creation.CreatedAt);
                    context.CreditCardStatements.Add(statement);
                    break;
                }

                if (statement.Status == CreditCardStatementStatus.Open)
                {
                    break;
                }

                cycle = cycle.Next(card.ClosingDay, card.DueDay);
            }
        }
        else if (statement is null)
        {
            statement = new CreditCardStatement(card, intendedCycle, creation.CreatedAt);
            context.CreditCardStatements.Add(statement);
        }

        var existingTotal = statement.Id == 0
            ? 0m
            : await context.FinancialTransactions
                .Where(item =>
                    item.StatementId == statement.Id &&
                    !item.IsDeleted)
                .Select(item => (decimal?)(item.Direction == TransactionDirection.Expense
                    ? item.Amount
                    : -item.Amount))
                .SumAsync(cancellationToken) ?? 0m;
        var charge = new FinancialTransaction(
            card.User,
            card,
            TransactionDirection.Expense,
            creation.Amount,
            creation.OccurredOn,
            creation.CreatedAt);
        charge.AssignToStatement(statement, isLateArriving, creation.CreatedAt);
        statement.RecalculatePurchaseTotal(existingTotal + creation.Amount, creation.CreatedAt);
        context.FinancialTransactions.Add(charge);
        await context.SaveChangesAsync(cancellationToken);

        return new CardChargeCreationResult(new CardChargeSnapshot(
            charge.PublicId,
            card.PublicId,
            charge.Amount,
            charge.OccurredOn,
            charge.IsLateArriving,
            statement.PublicId,
            statement.PeriodStart,
            statement.PeriodEnd,
            statement.ClosingDate,
            statement.DueDate,
            statement.Status.ToString(),
            statement.PurchaseTotal), CardNotFound: false);
    }
}
