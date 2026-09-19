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

public sealed class ListCommittedObligationsQueryHandler(
    IValidator<ListCommittedObligationsQuery> validator,
    ICurrentProfileResolver profileResolver,
    ICommittedObligationReader obligations,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
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

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(CommittedObligationMessages.ProfileNotFound);
        }

        var displayCode = DisplayCurrency.ResolveCode(query.DisplayCurrencyCode, profile);
        var displayCurrency = await currencies.FindByCodeAsync(displayCode, CancellationToken.None);
        if (displayCurrency is null)
        {
            return output.WithError(CommittedObligationMessages.DisplayCurrencyUnsupported);
        }

        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var through = asOf.AddDays(query.HorizonDays);
        var snapshots = await obligations.ReadAsync(
            profile.Id, asOf, through, CancellationToken.None);
        var converter = new FigureConverter(rates, displayCurrency);
        var converted = new List<ConvertedObligation>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            // Dated figures: each obligation converts at its own due date.
            var conversion = await converter.ConvertAsync(
                snapshot.CurrencyCode,
                snapshot.Amount,
                snapshot.DueDate);
            var isOverdue = snapshot.DueDate < asOf;
            converted.Add(new ConvertedObligation(conversion, new CommittedObligationOutput
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
                DisplayAmount = converter.Round(conversion.Value),
                AppliedRate = conversion.Rate?.Rate,
                RateDate = conversion.Rate?.RateDate,
                RateSource = conversion.Rate?.Source,
                UnconvertedReason = conversion.UnconvertedReason
            }));
        }

        converted = converted
            .OrderByDescending(item => item.Output.IsOverdue)
            .ThenBy(item => item.Output.DueDate)
            .ThenBy(item => item.Output.Kind)
            .ThenBy(item => item.Output.Id)
            .ToList();
        var total = converter.Total(converted.Select(item => item.Conversion));
        var periods = converted
            .GroupBy(item => new { item.Output.DueDate.Year, item.Output.DueDate.Month })
            .OrderBy(group => group.Key.Year)
            .ThenBy(group => group.Key.Month)
            .Select(group =>
            {
                var periodTotal = converter.Total(group.Select(item => item.Conversion));

                return new CommittedObligationPeriodOutput
                {
                    PeriodStart = new DateOnly(group.Key.Year, group.Key.Month, 1),
                    PeriodEnd = new DateOnly(
                        group.Key.Year,
                        group.Key.Month,
                        DateTime.DaysInMonth(group.Key.Year, group.Key.Month)),
                    IsFullyConverted = periodTotal.HasValue,
                    Total = periodTotal
                };
            })
            .ToArray();

        return output
            .WithData(new CommittedObligationListOutput
            {
                AsOf = asOf,
                Through = through,
                DisplayCurrencyCode = displayCurrency.Code,
                IsFullyConverted = total.HasValue,
                Total = total,
                Items = converted.Select(item => item.Output).ToArray(),
                Periods = periods,
                Rates = converter.AppliedRates
                    .Select(item => new CommittedObligationRateOutput
                    {
                        BaseCurrencyCode = item.BaseCurrencyCode,
                        QuoteCurrencyCode = item.QuoteCurrencyCode,
                        Rate = item.Rate,
                        RateDate = item.RateDate,
                        Source = item.Source
                    })
                    .ToArray(),
                MissingRates = MissingExchangeRateOutput.From(converter)
            })
            .WithMessage(total.HasValue
                ? CommittedObligationMessages.RetrievedSuccessfully
                : CommittedObligationMessages.PartiallyConverted);
    }

    private sealed record ConvertedObligation(
        FigureConversion Conversion,
        CommittedObligationOutput Output);
}
