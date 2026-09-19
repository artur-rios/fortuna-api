using System.Diagnostics;
using ArturRios.Fortuna.Query.Conversion;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Projections;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ProjectCashFlowQueryHandler(
    IValidator<ProjectCashFlowQuery> validator,
    ICurrentProfileResolver profileResolver,
    ICashFlowProjectionReader projections,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
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

        if (!Enum.IsDefined(query.Periodicity))
        {
            return output.WithError(CashFlowProjectionMessages.PeriodicityInvalid);
        }

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(CashFlowProjectionMessages.ProfileNotFound);
        }

        var displayCode = DisplayCurrency.ResolveCode(query.DisplayCurrencyCode, profile);
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
        var converter = new FigureConverter(rates, displayCurrency);

        // Balances are a point-in-time position (as-of date); flows convert at their own
        // date. A figure without a rate is left out of the balances and reported instead.
        var startingBalance = 0m;
        foreach (var balance in snapshot.StartingBalances)
        {
            var converted = await converter.ConvertAsync(
                balance.CurrencyCode, balance.Amount, asOf);
            startingBalance += converted.Value ?? 0m;
        }

        var future = new List<ConvertedFigure>(snapshot.FutureFigures.Count);
        foreach (var figure in snapshot.FutureFigures)
        {
            var converted = await converter.ConvertAsync(
                figure.CurrencyCode, figure.SignedAmount, figure.Date);
            if (converted.Value.HasValue)
            {
                future.Add(new ConvertedFigure(figure.Date, figure.Source, converted.Value.Value));
            }
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
                    var converted = await converter.ConvertAsync(
                        figure.CurrencyCode, figure.SignedAmount, figure.Date);
                    historyTotal += converted.Value ?? 0m;
                }

                estimatedDailyAmount = historyTotal / observedDays;
            }
        }

        var periods = new List<CashFlowPeriodOutput>();
        var opening = startingBalance;
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
            var figures = new List<CashFlowFigureOutput>();
            if (index == 0)
            {
                figures.Add(new CashFlowFigureOutput
                {
                    Kind = CashFlowFigureKind.Recorded,
                    Amount = converter.Round(opening)
                });
            }

            AddFigure(figures, CashFlowFigureKind.Projected, converter.Round(projected));
            AddFigure(figures, CashFlowFigureKind.Committed, converter.Round(committed));
            if (estimatedDailyAmount.HasValue)
            {
                figures.Add(new CashFlowFigureOutput
                {
                    Kind = CashFlowFigureKind.Estimated,
                    Amount = converter.Round(estimated)
                });
            }

            var closing = opening + projected + committed + estimated;
            periods.Add(new CashFlowPeriodOutput
            {
                PeriodStart = range.Start,
                PeriodEnd = range.End,
                OpeningBalance = converter.Round(opening),
                ClosingBalance = converter.Round(closing),
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
                StartingBalance = converter.Round(startingBalance),
                FlatReason = future.Count == 0 && !estimatedDailyAmount.HasValue
                    ? CashFlowProjectionMessages.NoProjectionInputs
                    : null,
                EstimateOmittedReason = estimateOmittedReason,
                Periods = periods,
                Rates = converter.AppliedRates
                    .Select(item => new CashFlowRateOutput
                    {
                        BaseCurrencyCode = item.BaseCurrencyCode,
                        QuoteCurrencyCode = item.QuoteCurrencyCode,
                        Rate = item.Rate,
                        RateDate = item.RateDate,
                        Source = item.Source
                    })
                    .ToArray(),
                IsFullyConverted = converter.IsFullyConverted,
                MissingRates = MissingExchangeRateOutput.From(converter)
            })
            .WithMessage(converter.IsFullyConverted
                ? CashFlowProjectionMessages.RetrievedSuccessfully
                : CashFlowProjectionMessages.PartiallyConverted);
    }

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
                _ => throw new UnreachableException()
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

    private sealed record ConvertedFigure(
        DateOnly Date,
        CashFlowSourceKind Source,
        decimal Amount);
}
