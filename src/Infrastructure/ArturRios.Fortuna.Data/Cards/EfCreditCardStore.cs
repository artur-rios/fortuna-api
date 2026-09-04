using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Shared.Cards;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Cards;

public sealed class EfCreditCardStore(AppDbContext context) : ICreditCardStore
{
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
}
