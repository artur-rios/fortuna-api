using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Shared.Projections;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Projections;

public sealed class EfCommittedObligationReader(AppDbContext context)
    : ICommittedObligationReader
{
    public async Task<IReadOnlyCollection<CommittedObligationSnapshot>> ReadAsync(
        Guid userId,
        DateOnly asOf,
        DateOnly through,
        CancellationToken cancellationToken)
    {
        var installments = await context.FinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.User.PublicId == userId &&
                transaction.InstallmentPlanId != null &&
                !transaction.IsDeleted &&
                !transaction.InstallmentPlan!.IsDeleted &&
                !transaction.CreditCard!.IsDeleted &&
                transaction.OccurredOn > asOf &&
                (transaction.Statement == null ||
                    transaction.Statement.Status == CreditCardStatementStatus.Open) &&
                (transaction.Statement == null
                    ? transaction.OccurredOn
                    : transaction.Statement.DueDate) <= through)
            .Select(transaction => new CommittedObligationSnapshot(
                transaction.PublicId,
                CommittedObligationKind.Installment,
                transaction.Statement == null
                    ? transaction.OccurredOn
                    : transaction.Statement.DueDate,
                transaction.Statement == null
                    ? null
                    : (DateOnly?)transaction.Statement.PeriodStart,
                transaction.Statement == null
                    ? null
                    : (DateOnly?)transaction.Statement.PeriodEnd,
                transaction.Currency.Code,
                transaction.Amount))
            .ToArrayAsync(cancellationToken);

        var statements = await context.CreditCardStatements
            .AsNoTracking()
            .Where(statement =>
                statement.CreditCard.User.PublicId == userId &&
                statement.Status == CreditCardStatementStatus.Closed &&
                !statement.IsDeleted &&
                !statement.CreditCard.IsDeleted &&
                statement.DueDate <= through &&
                statement.AmountDue != 0m)
            .Select(statement => new CommittedObligationSnapshot(
                statement.PublicId,
                CommittedObligationKind.Statement,
                statement.DueDate,
                statement.PeriodStart,
                statement.PeriodEnd,
                statement.CreditCard.Currency.Code,
                statement.AmountDue))
            .ToArrayAsync(cancellationToken);

        return installments
            .Concat(statements)
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Id)
            .ToArray();
    }
}
