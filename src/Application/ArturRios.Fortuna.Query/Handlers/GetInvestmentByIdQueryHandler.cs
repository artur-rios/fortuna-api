using ArturRios.Fortuna.Query.Conversion;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetInvestmentByIdQueryHandler(
    IValidator<GetInvestmentByIdQuery> validator,
    IUserProfileReader profiles,
    IInvestmentReader investments,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    IRequestActorAccessor actorAccessor,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<GetInvestmentByIdQuery, InvestmentOutput>
{
    public async Task<DataOutput<InvestmentOutput?>> HandleAsync(GetInvestmentByIdQuery query)
    {
        var output = DataOutput<InvestmentOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(InvestmentMessages.ProfileNotFound);
        }

        var investment = await investments.FindByIdWithPositionAsync(
            profile.Id,
            query.Id,
            CancellationToken.None);
        if (investment is null)
        {
            return output.WithError(InvestmentMessages.NotFound);
        }

        var displayCode = DisplayCurrency.ResolveCode(query.DisplayCurrencyCode, profile);
        var displayCurrency = await currencies.FindByCodeAsync(displayCode, CancellationToken.None);
        if (displayCurrency is null)
        {
            return output
                .WithError(InvestmentMessages.CurrencyNotSupported)
                .WithMessage(InvestmentMessages.UnknownCurrency(displayCode));
        }

        var result = InvestmentPositionProjection.Project(investment);
        await InvestmentPositionProjection.ApplyConversionAsync(
            result,
            new FigureConverter(rates, displayCurrency),
            query.FigureDate ?? Today());

        return output
            .WithData(result)
            .WithMessage(InvestmentMessages.RetrievedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}
