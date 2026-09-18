using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Cards;

internal sealed record StatementAssignment(CreditCardStatement Statement, bool IsLateArriving);

// Statement assignment and totals for card charges. Callers must run inside a database
// transaction: a missing statement is created and saved immediately so a concurrent creation of
// the same billing cycle surfaces as a unique violation that is resolved by re-reading.
internal static class CreditCardStatementResolver
{
    private const int CreationAttempts = 3;

    // The charge belongs to the statement of its billing cycle; when that one is already
    // settled it arrives late and moves to the first later statement that is still open.
    public static async Task<StatementAssignment> ResolveAsync(
        AppDbContext context,
        CreditCard card,
        DateOnly occurredOn,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var cycle = BillingCycle.Containing(occurredOn, card.ClosingDay, card.DueDay);
        var statement = await GetOrCreateAsync(context, card, cycle, changedAt, cancellationToken);
        if (statement.Status != CreditCardStatementStatus.Settled)
        {
            return new StatementAssignment(statement, false);
        }

        while (true)
        {
            cycle = cycle.Next(card.ClosingDay, card.DueDay);
            statement = await GetOrCreateAsync(context, card, cycle, changedAt, cancellationToken);
            if (statement.Status == CreditCardStatementStatus.Open)
            {
                return new StatementAssignment(statement, true);
            }
        }
    }

    public static async Task AssignAsync(
        AppDbContext context,
        FinancialTransaction transaction,
        CreditCard card,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var assignment = await ResolveAsync(
            context,
            card,
            transaction.OccurredOn,
            changedAt,
            cancellationToken);
        transaction.AssignToStatement(assignment.Statement, assignment.IsLateArriving, changedAt);
    }

    // Recomputes purchase totals from the database under a row lock on each statement, so two
    // concurrent changes to the same statement serialize instead of overwriting each other's
    // in-memory total. Pending changes are saved first so the sums include them.
    public static async Task RefreshTotalsAsync(
        AppDbContext context,
        IEnumerable<CreditCardStatement?> statements,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var targets = statements
            .OfType<CreditCardStatement>()
            .Distinct()
            .ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken);
        var ids = targets.Select(statement => statement.Id).ToArray();
        await RowLock.LockForUpdateAsync<CreditCardStatement>(context, ids, cancellationToken);
        var totals = await context.FinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.StatementId.HasValue &&
                ids.Contains(transaction.StatementId.Value) &&
                !transaction.IsDeleted)
            .GroupBy(transaction => transaction.StatementId!.Value)
            .Select(group => new
            {
                StatementId = group.Key,
                Total = group.Sum(transaction =>
                    transaction.Direction == TransactionDirection.Expense
                        ? transaction.Amount
                        : -transaction.Amount)
            })
            .ToDictionaryAsync(item => item.StatementId, item => item.Total, cancellationToken);
        foreach (var statement in targets)
        {
            statement.RecalculatePurchaseTotal(totals.GetValueOrDefault(statement.Id), changedAt);
        }
    }

    public static async Task<CreditCardStatement> GetOrCreateAsync(
        AppDbContext context,
        CreditCard card,
        BillingCycle cycle,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var existing = await FindAsync(context, card, cycle, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var statement = new CreditCardStatement(card, cycle, changedAt);
            context.CreditCardStatements.Add(statement);
            try
            {
                await context.SaveChangesAsync(cancellationToken);

                return statement;
            }
            catch (DbUpdateException exception) when (
                attempt < CreationAttempts &&
                DatabaseException.IsUniqueViolation(exception, CreditCardStatementMap.CycleIndex))
            {
                context.Entry(statement).State = EntityState.Detached;
            }
        }
    }

    private static Task<CreditCardStatement?> FindAsync(
        AppDbContext context,
        CreditCard card,
        BillingCycle cycle,
        CancellationToken cancellationToken) => context.CreditCardStatements
        .SingleOrDefaultAsync(statement =>
            statement.CreditCardId == card.Id &&
            statement.PeriodStart == cycle.PeriodStart &&
            statement.PeriodEnd == cycle.PeriodEnd &&
            !statement.IsDeleted,
            cancellationToken);
}
