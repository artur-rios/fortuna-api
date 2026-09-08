using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetNetPositionQueryHandler(
    IValidator<GetNetPositionQuery> validator,
    IUserProfileReader profiles,
    INetPositionReader positions,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    IRequestActorAccessor actorAccessor,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<GetNetPositionQuery, NetPositionOutput>
{
    public async Task<DataOutput<NetPositionOutput?>> HandleAsync(GetNetPositionQuery query)
    {
        var output = DataOutput<NetPositionOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(item => item.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(NetPositionMessages.ProfileNotFound);
        }

        var displayCode = string.IsNullOrWhiteSpace(query.DisplayCurrencyCode)
            ? profile.DisplayCurrency.ToUpperInvariant()
            : query.DisplayCurrencyCode.Trim().ToUpperInvariant();
        var displayCurrency = await currencies.FindByCodeAsync(
            displayCode,
            CancellationToken.None);
        if (displayCurrency is null)
        {
            return output
                .WithError(NetPositionMessages.DisplayCurrencyUnsupported)
                .WithMessage(NetPositionMessages.UnknownCurrency(displayCode));
        }

        var asOf = query.AsOf ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var sourceGroups = await positions.ReadAsync(profile.Id, asOf, CancellationToken.None);
        var groups = new List<NetPositionCurrencyOutput>(sourceGroups.Count);
        foreach (var source in sourceGroups.OrderBy(item => item.CurrencyCode, StringComparer.Ordinal))
        {
            var group = new NetPositionCurrencyOutput
            {
                SourceCurrencyCode = source.CurrencyCode,
                FinancialAccounts = source.FinancialAccounts,
                Investments = source.Investments,
                CreditCards = source.CreditCards,
                SourceNet = source.Net
            };
            if (source.CurrencyCode == displayCurrency.Code)
            {
                group.DisplayNet = Round(source.Net, displayCurrency.MinorUnitDigits);
            }
            else
            {
                var rate = await rates.FindApplicableAsync(
                    source.CurrencyCode,
                    displayCurrency.Code,
                    asOf,
                    CancellationToken.None);
                if (rate is null)
                {
                    group.UnconvertedReason = FigureConversionMessages.RateUnavailable;
                }
                else
                {
                    group.DisplayNet = Round(source.Net * rate.Rate,
                        displayCurrency.MinorUnitDigits);
                    group.AppliedRate = rate.Rate;
                    group.RateDate = rate.RateDate;
                    group.RateSource = rate.Source;
                }
            }

            groups.Add(group);
        }

        var fullyConverted = groups.All(item => item.DisplayNet.HasValue);
        return output
            .WithData(new NetPositionOutput
            {
                AsOf = asOf,
                DisplayCurrencyCode = displayCurrency.Code,
                Total = fullyConverted
                    ? Round(groups.Sum(item => item.DisplayNet!.Value),
                        displayCurrency.MinorUnitDigits)
                    : null,
                IsFullyConverted = fullyConverted,
                CurrencyGroups = groups
            })
            .WithMessage(fullyConverted
                ? NetPositionMessages.RetrievedSuccessfully
                : NetPositionMessages.PartiallyConverted);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private static decimal Round(decimal value, short digits) =>
        decimal.Round(value, digits, MidpointRounding.AwayFromZero);
}
