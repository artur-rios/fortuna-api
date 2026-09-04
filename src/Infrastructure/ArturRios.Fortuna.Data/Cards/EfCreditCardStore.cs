using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Shared.Cards;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Cards;

public sealed class EfCreditCardStore(AppDbContext context)
    : ICreditCardStore, ICreditCardReader, ICreditCardUpdater, ICreditCardLifecycleStore
{
    public IQueryable<CreditCardLimitSnapshot> QueryLimits() => context.CreditCards
        .AsNoTracking()
        .Select(card => new CreditCardLimitSnapshot
        {
            Id = card.PublicId,
            UserId = card.User.PublicId,
            Name = card.Name,
            Issuer = card.Issuer,
            CurrencyCode = card.Currency.Code,
            CreditLimit = card.CreditLimit,
            ClosingDay = card.ClosingDay,
            DueDay = card.DueDay,
            LastFourDigits = card.LastFourDigits,
            OutstandingAmount = context.FinancialTransactions
                .Where(transaction => transaction.CreditCardId == card.Id && !transaction.IsDeleted)
                .Select(transaction => (decimal?)(transaction.Direction ==
                    Domain.Transactions.TransactionDirection.Expense
                        ? transaction.Amount
                        : -transaction.Amount))
                .Sum() ?? 0m,
            IsDeleted = card.IsDeleted,
            CreatedAt = card.CreatedAt,
            UpdatedAt = card.UpdatedAt
        });

    public Task<CreditCardLimitSnapshot?> FindByIdWithLimitsAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => QueryLimits()
        .SingleOrDefaultAsync(card =>
            card.UserId == userId &&
            card.Id == id &&
            !card.IsDeleted,
            cancellationToken);

    public async Task<CreditCardCreationResult> CreateAsync(
        CreditCardCreation creation,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            profile => profile.PublicId == creation.UserId,
            cancellationToken);
        var currency = await context.Currencies.SingleAsync(
            item => item.Code == creation.CurrencyCode,
            cancellationToken);
        var card = new CreditCard(
            user,
            creation.Name,
            creation.Issuer,
            currency,
            creation.CreditLimit,
            creation.ClosingDay,
            creation.DueDay,
            creation.LastFourDigits,
            creation.CreatedAt);
        context.CreditCards.Add(card);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CreditCardMap.LiveNameIndex
            })
        {
            context.Entry(card).State = EntityState.Detached;
            return new CreditCardCreationResult(null, DuplicateName: true);
        }

        return new CreditCardCreationResult(new CreditCardSnapshot(
            card.PublicId,
            card.User.PublicId,
            card.Name,
            card.Issuer,
            card.Currency.Code,
            card.CreditLimit,
            card.ClosingDay,
            card.DueDay,
            card.LastFourDigits,
            card.IsDeleted,
            card.CreatedAt,
            card.UpdatedAt), DuplicateName: false);
    }

    public async Task<CreditCardUpdateResult> UpdateAsync(
        CreditCardUpdate update,
        CancellationToken cancellationToken)
    {
        var card = await context.CreditCards
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleOrDefaultAsync(item =>
                item.User.PublicId == update.UserId &&
                item.PublicId == update.Id &&
                !item.IsDeleted,
                cancellationToken);
        if (card is null)
        {
            return new CreditCardUpdateResult(null, DuplicateName: false);
        }

        card.UpdateDetails(
            update.Name,
            update.Issuer,
            update.CreditLimit,
            update.ClosingDay,
            update.DueDay,
            update.UpdatedAt);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CreditCardMap.LiveNameIndex
            })
        {
            context.Entry(card).State = EntityState.Detached;
            return new CreditCardUpdateResult(null, DuplicateName: true);
        }

        return new CreditCardUpdateResult(Snapshot(card), DuplicateName: false);
    }

    public async Task<CreditCardLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var card = await FindTrackedAsync(userId, id, cancellationToken);
        if (card is null)
        {
            return LifecycleResult(CreditCardLifecycleOutcome.NotFound);
        }

        var transactions = await CardTransactionsAsync(card.Id, cancellationToken);
        var outstandingAmount = CalculateOutstandingAmount(transactions);
        var deletion = card.SoftDelete(changedAt);
        foreach (var transaction in transactions)
        {
            transaction.SoftDeleteFromCascade(deletion.CascadeId, changedAt);
        }

        await context.SaveChangesAsync(cancellationToken);
        return LifecycleResult(
            CreditCardLifecycleOutcome.Succeeded,
            card.PublicId,
            card.Currency.Code,
            outstandingAmount);
    }

    public async Task<CreditCardLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var card = await FindTrackedAsync(userId, id, cancellationToken);
        if (card is null)
        {
            return LifecycleResult(CreditCardLifecycleOutcome.NotFound);
        }

        if (!card.IsDeleted)
        {
            return LifecycleResult(CreditCardLifecycleOutcome.RestoreRequiresSoftDeletion);
        }

        var transactions = await CardTransactionsAsync(card.Id, cancellationToken);
        var cascadeId = card.Restore(changedAt);
        foreach (var transaction in transactions)
        {
            transaction.RestoreFromCascade(cascadeId, changedAt);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CreditCardMap.LiveNameIndex
            })
        {
            context.Entry(card).State = EntityState.Detached;
            foreach (var transaction in transactions)
            {
                context.Entry(transaction).State = EntityState.Detached;
            }

            return LifecycleResult(CreditCardLifecycleOutcome.DuplicateName);
        }

        return LifecycleResult(
            CreditCardLifecycleOutcome.Succeeded,
            card.PublicId,
            card.Currency.Code,
            CalculateOutstandingAmount(transactions));
    }

    public async Task<CreditCardLifecycleResult> HardDeleteAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var card = await FindTrackedAsync(userId, id, cancellationToken);
        if (card is null)
        {
            return LifecycleResult(CreditCardLifecycleOutcome.NotFound);
        }

        var transactions = await CardTransactionsAsync(card.Id, cancellationToken);
        var liveReferences = transactions.Any(item => !item.IsDeleted)
            ? new[] { "transactions" }
            : [];
        try
        {
            card.EnsureHardDeletionAllowed(liveReferences);
        }
        catch (RecordLifecycleConflictException exception)
        {
            return exception.Conflict switch
            {
                RecordLifecycleConflict.HardDeleteRequiresSoftDeletion =>
                    LifecycleResult(CreditCardLifecycleOutcome.HardDeleteRequiresSoftDeletion),
                RecordLifecycleConflict.HardDeleteHasLiveReferences =>
                    LifecycleResult(CreditCardLifecycleOutcome.HardDeleteHasLiveTransactions),
                _ => throw new InvalidOperationException(
                    "An unexpected lifecycle conflict prevented hard deletion.",
                    exception)
            };
        }

        var outstandingAmount = CalculateOutstandingAmount(transactions);
        var currencyCode = card.Currency.Code;
        context.FinancialTransactions.RemoveRange(transactions);
        context.CreditCards.Remove(card);
        await context.SaveChangesAsync(cancellationToken);

        return LifecycleResult(
            CreditCardLifecycleOutcome.Succeeded,
            card.PublicId,
            currencyCode,
            outstandingAmount);
    }

    private Task<CreditCard?> FindTrackedAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => context.CreditCards
        .Include(item => item.User)
        .Include(item => item.Currency)
        .SingleOrDefaultAsync(item =>
            item.User.PublicId == userId &&
            item.PublicId == id,
            cancellationToken);

    private Task<List<Domain.Transactions.FinancialTransaction>> CardTransactionsAsync(
        long cardId,
        CancellationToken cancellationToken) => context.FinancialTransactions
        .Where(item => item.CreditCardId == cardId)
        .ToListAsync(cancellationToken);

    private static decimal CalculateOutstandingAmount(
        IEnumerable<Domain.Transactions.FinancialTransaction> transactions) => Math.Max(
        0m,
        transactions
            .Where(item => !item.IsDeleted)
            .Sum(item => item.Direction == Domain.Transactions.TransactionDirection.Expense
                ? item.Amount
                : -item.Amount));

    private static CreditCardLifecycleResult LifecycleResult(
        CreditCardLifecycleOutcome outcome,
        Guid? id = null,
        string? currencyCode = null,
        decimal outstandingAmount = 0m) => new(id, outcome, currencyCode, outstandingAmount);

    private static CreditCardSnapshot Snapshot(CreditCard card) => new(
        card.PublicId,
        card.User.PublicId,
        card.Name,
        card.Issuer,
        card.Currency.Code,
        card.CreditLimit,
        card.ClosingDay,
        card.DueDay,
        card.LastFourDigits,
        card.IsDeleted,
        card.CreatedAt,
        card.UpdatedAt);
}
