using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetCreditCardByIdQueryHandler(
    IUserProfileReader profiles,
    ICreditCardReader cards,
    IRequestActorAccessor actorAccessor)
    : IQueryHandlerAsync<GetCreditCardByIdQuery, CreditCardOutput>
{
    public async Task<DataOutput<CreditCardOutput?>> HandleAsync(GetCreditCardByIdQuery query)
    {
        var output = DataOutput<CreditCardOutput?>.New;
        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(CreditCardMessages.ProfileNotFound);
        }

        var card = await cards.FindByIdWithLimitsAsync(
            profile.Id,
            query.Id,
            CancellationToken.None);
        if (card is null)
        {
            return output.WithError(CreditCardMessages.NotFound);
        }

        return output
            .WithData(Project(card))
            .WithMessage(CreditCardMessages.RetrievedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    internal static CreditCardOutput Project(CreditCardLimitSnapshot card)
    {
        var used = Math.Max(card.OutstandingAmount, 0m);
        return new CreditCardOutput
        {
            Id = card.Id,
            Name = card.Name,
            Issuer = card.Issuer,
            CurrencyCode = card.CurrencyCode,
            CreditLimit = card.CreditLimit,
            UsedAmount = used,
            AvailableAmount = Math.Max(card.CreditLimit - used, 0m),
            OverageAmount = Math.Max(used - card.CreditLimit, 0m),
            ClosingDay = card.ClosingDay,
            DueDay = card.DueDay,
            LastFourDigits = card.LastFourDigits,
            CreatedAt = card.CreatedAt,
            UpdatedAt = card.UpdatedAt
        };
    }
}
