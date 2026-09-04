using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Shared.Cards;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Cards;

public sealed class EfCreditCardStore(AppDbContext context)
    : ICreditCardStore, ICreditCardReader, ICreditCardUpdater
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
