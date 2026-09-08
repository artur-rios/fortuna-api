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

public sealed class ProjectCashFlowQueryHandler(
    IValidator<ProjectCashFlowQuery> validator,
    IUserProfileReader profiles,
    ICashFlowProjectionReader projections,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    IRequestActorAccessor actorAccessor,
    TimeProvider timeProvider,
    CashFlowProjectionOptions options)
    : IQueryHandlerAsync<ProjectCashFlowQuery, CashFlowProjectionOutput>
{
    public async Task<DataOutput<CashFlowProjectionOutput?>> HandleAsync(ProjectCashFlowQuery query)
    {
        var output = DataOutput<CashFlowProjectionOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(item => item.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(CashFlowProjectionMessages.ProfileNotFound);
        }

        var displayCode = string.IsNullOrWhiteSpace(query.DisplayCurrencyCode)
            ? profile.DisplayCurrency.ToUpperInvariant()
            : query.DisplayCurrencyCode.Trim().ToUpperInvariant();
        var displayCurrency = await currencies.FindByCodeAsync(displayCode, CancellationToken.None);
        if (displayCurrency is null)
        {
            return output.WithError(CashFlowProjectionMessages.DisplayCurrencyUnsupported);
        }

        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var through = asOf.AddDays(query.HorizonDays);
        var historyFrom = asOf.AddDays(-(options.HistoricalLookbackDays - 1));
        var snapshot = await projections.ReadAsync(
            profile.Id, asOf, through, historyFrom, CancellationToken.None);
        var rateCache = new Dictionary<(string Currency, DateOnly Date), ExchangeRateSnapshot?>();

        async Task<decimal?> ConvertAsync(string currencyCode, decimal amount, DateOnly date)
        {
            if (currencyCode == displayCurrency.Code)
            {
                return amount;
            }

            var key = (currencyCode, date);
            if (!rateCache.TryGetValue(key, out var rate))
            {
                rate = await rates.FindApplicableAsync(
                    currencyCode, displayCurrency.Code, date, CancellationToken.None);
                rateCache[key] = rate;
            }

            return rate is null ? null : amount * rate.Rate;
        }

        var startingBalance = 0m;
        foreach (var balance in snapshot.StartingBalances)
        {
            var converted = await ConvertAsync(balance.CurrencyCode, balance.Amount, asOf);
            if (!converted.HasValue)
            {
                return output.WithError(CashFlowProjectionMessages.ExchangeRateUnavailable);
            }

            startingBalance += converted.Value;
        }

        var future = new List<ConvertedFigure>(snapshot.FutureFigures.Count);
        foreach (var figure in snapshot.FutureFigures)
        {
            var converted = await ConvertAsync(figure.CurrencyCode, figure.SignedAmount, figure.Date);
            if (!converted.HasValue)
            {
                return output.WithError(CashFlowProjectionMessages.ExchangeRateUnavailable);
            }

            future.Add(new ConvertedFigure(figure.Date, figure.Source, converted.Value));
        }

        decimal? estimatedDailyAmount = null;
        string? estimateOmittedReason = null;
        if (query.IncludeEstimate)
        {
            var observedFrom = snapshot.HistoryStartsOn.HasValue
                ? DateOnly.FromDayNumber(Math.Max(
                    snapshot.HistoryStartsOn.Value.DayNumber,
                    historyFrom.DayNumber))
                : (DateOnly?)null;
            var observedDays = observedFrom.HasValue
                ? asOf.DayNumber - observedFrom.Value.DayNumber + 1
                : 0;
            if (observedDays < options.MinimumHistoryDays)
            {
                estimateOmittedReason = CashFlowProjectionMessages.InsufficientHistory;
            }
            else
            {
                var historyTotal = 0m;
                foreach (var figure in snapshot.HistoricalFigures)
                {
                    var converted = await ConvertAsync(
                        figure.CurrencyCode, figure.SignedAmount, figure.Date);
                    if (!converted.HasValue)
                    {
                        return output.WithError(CashFlowProjectionMessages.ExchangeRateUnavailable);
                    }

                    historyTotal += converted.Value;
                }

                estimatedDailyAmount = historyTotal / observedDays;
            }
        }

        var periods = new List<CashFlowPeriodOutput>();
        var opening = Round(startingBalance, displayCurrency.MinorUnitDigits);
        var ranges = PeriodRanges(asOf.AddDays(1), through, query.Periodicity);
        for (var index = 0; index < ranges.Count; index++)
        {
            var range = ranges[index];
            var inPeriod = future.Where(item =>
                item.Date >= range.Start && item.Date <= range.End).ToArray();
            var projected = inPeriod
                .Where(item => item.Source == CashFlowSourceKind.Recurring)
                .Sum(item => item.Amount);
            var committed = inPeriod
                .Where(item => item.Source is CashFlowSourceKind.Installment or
                    CashFlowSourceKind.Statement)
                .Sum(item => item.Amount);
            var estimated = estimatedDailyAmount.HasValue
                ? estimatedDailyAmount.Value * (range.End.DayNumber - range.Start.DayNumber + 1)
                : 0m;
            projected = Round(projected, displayCurrency.MinorUnitDigits);
            committed = Round(committed, displayCurrency.MinorUnitDigits);
            estimated = Round(estimated, displayCurrency.MinorUnitDigits);
            var figures = new List<CashFlowFigureOutput>();
            if (index == 0)
            {
                figures.Add(new CashFlowFigureOutput
                {
                    Kind = CashFlowFigureKind.Recorded,
                    Amount = opening
                });
            }

            AddFigure(figures, CashFlowFigureKind.Projected, projected);
            AddFigure(figures, CashFlowFigureKind.Committed, committed);
            if (estimatedDailyAmount.HasValue)
            {
                figures.Add(new CashFlowFigureOutput
                {
                    Kind = CashFlowFigureKind.Estimated,
                    Amount = estimated
                });
            }

            var closing = Round(opening + projected + committed + estimated,
                displayCurrency.MinorUnitDigits);
            periods.Add(new CashFlowPeriodOutput
            {
                PeriodStart = range.Start,
                PeriodEnd = range.End,
                OpeningBalance = opening,
                ClosingBalance = closing,
                Figures = figures
            });
            opening = closing;
        }

        return output
            .WithData(new CashFlowProjectionOutput
            {
                AsOf = asOf,
                Through = through,
                DisplayCurrencyCode = displayCurrency.Code,
                Periodicity = query.Periodicity,
                StartingBalance = Round(startingBalance, displayCurrency.MinorUnitDigits),
                FlatReason = future.Count == 0 && !estimatedDailyAmount.HasValue
                    ? CashFlowProjectionMessages.NoProjectionInputs
                    : null,
                EstimateOmittedReason = estimateOmittedReason,
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
                    .Select(item => new CashFlowRateOutput
                    {
                        BaseCurrencyCode = item.BaseCurrencyCode,
                        QuoteCurrencyCode = item.QuoteCurrencyCode,
                        Rate = item.Rate,
                        RateDate = item.RateDate,
                        Source = item.Source
                    })
                    .ToArray()
            })
            .WithMessage(CashFlowProjectionMessages.RetrievedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private static IReadOnlyList<(DateOnly Start, DateOnly End)> PeriodRanges(
        DateOnly first, DateOnly through, CashFlowPeriodicity periodicity)
    {
        var ranges = new List<(DateOnly, DateOnly)>();
        for (var start = first; start <= through;)
        {
            var candidate = periodicity switch
            {
                CashFlowPeriodicity.Daily => start,
                CashFlowPeriodicity.Weekly => start.AddDays(6),
                CashFlowPeriodicity.Monthly => new DateOnly(
                    start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month)),
                _ => throw new InvalidOperationException("Unsupported cash-flow periodicity.")
            };
            var end = candidate > through ? through : candidate;
            ranges.Add((start, end));
            start = end.AddDays(1);
        }

        return ranges;
    }

    private static void AddFigure(
        ICollection<CashFlowFigureOutput> figures,
        CashFlowFigureKind kind,
        decimal amount)
    {
        if (amount != 0m)
        {
            figures.Add(new CashFlowFigureOutput { Kind = kind, Amount = amount });
        }
    }

    private static decimal Round(decimal value, short digits) =>
        decimal.Round(value, digits, MidpointRounding.AwayFromZero);

    private sealed record ConvertedFigure(
        DateOnly Date,
        CashFlowSourceKind Source,
        decimal Amount);
}
