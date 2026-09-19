using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetCreditCardByIdQueryHandler(
    IValidator<GetCreditCardByIdQuery> validator,
    ICurrentProfileResolver profileResolver,
    ICreditCardReader cards)
    : IQueryHandlerAsync<GetCreditCardByIdQuery, CreditCardOutput>
{
    public async Task<DataOutput<CreditCardOutput?>> HandleAsync(GetCreditCardByIdQuery query)
    {
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return DataOutput<CreditCardOutput?>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var output = DataOutput<CreditCardOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
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
            .WithData(CreditCardProjection.From(card))
            .WithMessage(CreditCardMessages.RetrievedSuccessfully);
    }
}
