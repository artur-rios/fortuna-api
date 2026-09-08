using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Projections;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListCommittedObligationsQueryHandler(
    IValidator<ListCommittedObligationsQuery> validator,
    IUserProfileReader profiles,
    ICommittedObligationReader obligations,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    IRequestActorAccessor actorAccessor,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<ListCommittedObligationsQuery, CommittedObligationListOutput>
{
    public async Task<DataOutput<CommittedObligationListOutput?>> HandleAsync(
        ListCommittedObligationsQuery query)
    {
        var output = DataOutput<CommittedObligationListOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(item => item.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(CommittedObligationMessages.ProfileNotFound);
        }

        var displayCode = string.IsNullOrWhiteSpace(query.DisplayCurrencyCode)
            ? profile.DisplayCurrency.ToUpperInvariant()
            : query.DisplayCurrencyCode.Trim().ToUpperInvariant();
        var displayCurrency = await currencies.FindByCodeAsync(displayCode, CancellationToken.None);
        if (displayCurrency is null)
        {
            return output.WithError(CommittedObligationMessages.DisplayCurrencyUnsupported);
        }

        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var through = asOf.AddDays(query.HorizonDays);
        var snapshots = await obligations.ReadAsync(
            profile.Id, asOf, through, CancellationToken.None);
        var rateCache = new Dictionary<(string Currency, DateOnly Date), ExchangeRateSnapshot?>();
        var items = new List<CommittedObligationOutput>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            ExchangeRateSnapshot? rate = null;
            decimal? converted;
            if (snapshot.CurrencyCode == displayCurrency.Code)
            {
                converted = Round(snapshot.Amount, displayCurrency.MinorUnitDigits);
            }
            else
            {
                var key = (snapshot.CurrencyCode, snapshot.DueDate);
                if (!rateCache.TryGetValue(key, out rate))
                {
                    rate = await rates.FindApplicableAsync(
                        snapshot.CurrencyCode,
                        displayCurrency.Code,
                        snapshot.DueDate,
                        CancellationToken.None);
                    rateCache[key] = rate;
                }

                converted = rate is null
                    ? null
                    : Round(snapshot.Amount * rate.Rate, displayCurrency.MinorUnitDigits);
            }

            var isOverdue = snapshot.DueDate < asOf;
            items.Add(new CommittedObligationOutput
            {
                Id = snapshot.Id,
                Kind = snapshot.Kind,
                DueDate = snapshot.DueDate,
                CycleStart = snapshot.CycleStart,
                CycleEnd = snapshot.CycleEnd,
                IsOverdue = isOverdue,
                DaysOverdue = isOverdue ? asOf.DayNumber - snapshot.DueDate.DayNumber : 0,
                CurrencyCode = snapshot.CurrencyCode,
                Amount = snapshot.Amount,
                DisplayAmount = converted,
                AppliedRate = rate?.Rate,
                RateDate = rate?.RateDate,
                RateSource = rate?.Source,
                UnconvertedReason = converted.HasValue
                    ? null
                    : FigureConversionMessages.RateUnavailable
            });
        }

        items = items
            .OrderByDescending(item => item.IsOverdue)
            .ThenBy(item => item.DueDate)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Id)
            .ToList();
        var fullyConverted = items.All(item => item.DisplayAmount.HasValue);
        var periods = items
            .GroupBy(item => new { item.DueDate.Year, item.DueDate.Month })
            .OrderBy(group => group.Key.Year)
            .ThenBy(group => group.Key.Month)
            .Select(group =>
            {
                var periodConverted = group.All(item => item.DisplayAmount.HasValue);
                return new CommittedObligationPeriodOutput
                {
                    PeriodStart = new DateOnly(group.Key.Year, group.Key.Month, 1),
                    PeriodEnd = new DateOnly(
                        group.Key.Year,
                        group.Key.Month,
                        DateTime.DaysInMonth(group.Key.Year, group.Key.Month)),
                    IsFullyConverted = periodConverted,
                    Total = periodConverted
                        ? Round(group.Sum(item => item.DisplayAmount!.Value),
                            displayCurrency.MinorUnitDigits)
                        : null
                };
            })
            .ToArray();

        return output
            .WithData(new CommittedObligationListOutput
            {
                AsOf = asOf,
                Through = through,
                DisplayCurrencyCode = displayCurrency.Code,
                IsFullyConverted = fullyConverted,
                Total = fullyConverted
                    ? Round(items.Sum(item => item.DisplayAmount!.Value),
                        displayCurrency.MinorUnitDigits)
                    : null,
                Items = items,
                Periods = periods,
                Rates = rateCache.Values
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .DistinctBy(item => new
                    {
                        item.BaseCurrencyCode,
                        item.QuoteCurrencyCode,
                        item.Rate,
                        item.RateDate,
                        item.Source
                    })
                    .Select(item => new CommittedObligationRateOutput
                    {
                        BaseCurrencyCode = item.BaseCurrencyCode,
                        QuoteCurrencyCode = item.QuoteCurrencyCode,
                        Rate = item.Rate,
                        RateDate = item.RateDate,
                        Source = item.Source
                    })
                    .ToArray()
            })
            .WithMessage(fullyConverted
                ? CommittedObligationMessages.RetrievedSuccessfully
                : CommittedObligationMessages.PartiallyConverted);
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
