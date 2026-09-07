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

public sealed class AggregateTransactionsQueryHandler(
    IValidator<AggregateTransactionsQuery> validator,
    IUserProfileReader profiles,
    ITransactionAggregationReader aggregations,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    IRequestActorAccessor actorAccessor,
    ITransactionDrillDownKeyCodec keyCodec,
    TransactionDrillDownOptions drillDownOptions,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<AggregateTransactionsQuery, TransactionAggregationOutput>
{
    public async Task<DataOutput<TransactionAggregationOutput?>> HandleAsync(
        AggregateTransactionsQuery query)
    {
        var output = DataOutput<TransactionAggregationOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(TransactionAggregationMessages.ProfileNotFound);
        }

        var displayCurrencyCode = string.IsNullOrWhiteSpace(query.DisplayCurrencyCode)
            ? profile.DisplayCurrency.ToUpperInvariant()
            : query.DisplayCurrencyCode.Trim().ToUpperInvariant();
        var displayCurrency = await currencies.FindByCodeAsync(
            displayCurrencyCode,
            CancellationToken.None);
        if (displayCurrency is null)
        {
            return output
                .WithError(TransactionAggregationMessages.DisplayCurrencyUnsupported)
                .WithMessage(TransactionAggregationMessages.UnknownCurrency(displayCurrencyCode));
        }

        var dimension = query.Dimension.Trim().ToLowerInvariant();
        var granularity = string.IsNullOrWhiteSpace(query.Granularity)
            ? null
            : query.Granularity.Trim().ToLowerInvariant();
        var from = query.From!.Value;
        var to = query.To!.Value;
        var snapshotAt = timeProvider.GetUtcNow();
        var criteria = new TransactionAggregationCriteria(
            profile.Id,
            dimension,
            granularity,
            from,
            to,
            query.RollupCategories,
            query.FinancialAccountId,
            query.CreditCardId,
            query.CategoryId,
            query.TagId,
            query.CounterpartyId,
            query.Direction,
            query.MinimumAmount,
            query.MaximumAmount,
            string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim(),
            query.Selections);
        var figures = await aggregations.ReadAsync(criteria, CancellationToken.None);
        var buckets = await BuildBucketsAsync(
            figures,
            criteria,
            displayCurrency,
            snapshotAt);
        ApplyShares(buckets);

        return output
            .WithData(new TransactionAggregationOutput
            {
                Dimension = dimension,
                Granularity = granularity,
                From = from,
                To = to,
                DisplayCurrencyCode = displayCurrency.Code,
                IsFullyConverted = buckets.All(bucket => bucket.IsFullyConverted),
                Buckets = buckets
            })
            .WithMessage(TransactionAggregationMessages.RetrievedSuccessfully);
    }

    private async Task<List<TransactionAggregationBucketOutput>> BuildBucketsAsync(
        IReadOnlyCollection<TransactionAggregationFigureSnapshot> figures,
        TransactionAggregationCriteria criteria,
        CurrencySnapshot displayCurrency,
        DateTimeOffset snapshotAt)
    {
        var source = figures
            .GroupBy(figure => new BucketIdentity(
                figure.DimensionValue,
                figure.BucketStart.HasValue && criteria.Dimension == "period"
                    ? PeriodLabel(figure.BucketStart.Value, criteria.Granularity!)
                    : figure.Label,
                figure.BucketStart))
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (criteria.Dimension == "period")
        {
            foreach (var start in PeriodStarts(criteria.From, criteria.To, criteria.Granularity!))
            {
                var identity = new BucketIdentity(
                    start.ToString("yyyy-MM-dd"),
                    PeriodLabel(start, criteria.Granularity!),
                    start);
                source.TryAdd(identity, []);
            }
        }

        var ordered = criteria.Dimension == "period"
            ? source.OrderBy(item => item.Key.BucketStart)
            : source.OrderBy(item => item.Key.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Key.DimensionValue, StringComparer.Ordinal);
        var rateCache = new Dictionary<(string CurrencyCode, DateOnly FigureDate),
            ExchangeRateSnapshot?>();
        var result = new List<TransactionAggregationBucketOutput>(source.Count);
        foreach (var (identity, bucketFigures) in ordered)
        {
            var conversions = new List<TransactionAggregationConversionOutput>();
            foreach (var figure in bucketFigures
                .GroupBy(item => (item.CurrencyCode, item.FigureDate))
                .Select(group => new
                {
                    group.Key.CurrencyCode,
                    group.Key.FigureDate,
                    Amount = group.Sum(item => item.Amount)
                })
                .OrderBy(item => item.CurrencyCode, StringComparer.Ordinal)
                .ThenBy(item => item.FigureDate))
            {
                conversions.Add(await ConvertAsync(figure.CurrencyCode,
                    figure.FigureDate,
                    figure.Amount,
                    displayCurrency.Code,
                    rateCache));
            }

            var fullyConverted = conversions.All(item => item.DisplayAmount.HasValue);
            var periodStart = identity.BucketStart.HasValue
                ? DateOnly.FromDayNumber(Math.Max(
                    identity.BucketStart.Value.DayNumber,
                    criteria.From.DayNumber))
                : (DateOnly?)null;
            var periodEnd = identity.BucketStart.HasValue
                ? DateOnly.FromDayNumber(Math.Min(
                    PeriodEnd(identity.BucketStart.Value, criteria.Granularity!).DayNumber,
                    criteria.To.DayNumber))
                : (DateOnly?)null;
            result.Add(new TransactionAggregationBucketOutput
            {
                Label = identity.BucketStart.HasValue
                    ? PeriodLabel(identity.BucketStart.Value, criteria.Granularity!)
                    : identity.Label,
                Total = fullyConverted
                    ? decimal.Round(
                        conversions.Sum(item => item.DisplayAmount!.Value),
                        displayCurrency.MinorUnitDigits,
                        MidpointRounding.AwayFromZero)
                    : null,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                DrillDownKey = EncodeKey(criteria, identity.DimensionValue,
                    periodStart, periodEnd, bucketFigures.Sum(item => item.RecordCount),
                    snapshotAt, displayCurrency.Code),
                IsFullyConverted = fullyConverted,
                Conversions = conversions
            });
        }

        return result;
    }

    private async Task<TransactionAggregationConversionOutput> ConvertAsync(
        string sourceCurrency,
        DateOnly figureDate,
        decimal sourceAmount,
        string displayCurrency,
        IDictionary<(string CurrencyCode, DateOnly FigureDate), ExchangeRateSnapshot?> cache)
    {
        if (sourceCurrency == displayCurrency)
        {
            return new TransactionAggregationConversionOutput
            {
                SourceCurrencyCode = sourceCurrency,
                SourceAmount = sourceAmount,
                FigureDate = figureDate,
                DisplayAmount = sourceAmount
            };
        }

        var key = (sourceCurrency, figureDate);
        if (!cache.TryGetValue(key, out var rate))
        {
            rate = await rates.FindApplicableAsync(
                sourceCurrency,
                displayCurrency,
                figureDate,
                CancellationToken.None);
            cache[key] = rate;
        }

        return rate is null
            ? new TransactionAggregationConversionOutput
            {
                SourceCurrencyCode = sourceCurrency,
                SourceAmount = sourceAmount,
                FigureDate = figureDate,
                UnconvertedReason = FigureConversionMessages.RateUnavailable
            }
            : new TransactionAggregationConversionOutput
            {
                SourceCurrencyCode = sourceCurrency,
                SourceAmount = sourceAmount,
                FigureDate = figureDate,
                DisplayAmount = sourceAmount * rate.Rate,
                AppliedRate = rate.Rate,
                RateDate = rate.RateDate,
                RateSource = rate.Source
            };
    }

    private static void ApplyShares(IReadOnlyCollection<TransactionAggregationBucketOutput> buckets)
    {
        if (buckets.Any(bucket => !bucket.Total.HasValue))
        {
            return;
        }

        var denominator = buckets.Sum(bucket => Math.Abs(bucket.Total!.Value));
        foreach (var bucket in buckets)
        {
            bucket.Share = denominator == 0m ? 0m : Math.Abs(bucket.Total!.Value) / denominator;
        }
    }

    private static IEnumerable<DateOnly> PeriodStarts(
        DateOnly from,
        DateOnly to,
        string granularity)
    {
        for (var current = PeriodStart(from, granularity);
             current <= to;
             current = NextPeriod(current, granularity))
        {
            yield return current;
        }
    }

    private static DateOnly PeriodStart(DateOnly date, string granularity) => granularity switch
    {
        "day" => date,
        "week" => date.AddDays(-WeekdayOffset(date)),
        "month" => new DateOnly(date.Year, date.Month, 1),
        "quarter" => new DateOnly(date.Year, ((date.Month - 1) / 3 * 3) + 1, 1),
        "year" => new DateOnly(date.Year, 1, 1),
        _ => throw new InvalidOperationException("The aggregation granularity was not normalized.")
    };

    private static DateOnly NextPeriod(DateOnly start, string granularity) => granularity switch
    {
        "day" => start.AddDays(1),
        "week" => start.AddDays(7),
        "month" => start.AddMonths(1),
        "quarter" => start.AddMonths(3),
        "year" => start.AddYears(1),
        _ => throw new InvalidOperationException("The aggregation granularity was not normalized.")
    };

    private static DateOnly PeriodEnd(DateOnly start, string granularity) =>
        NextPeriod(start, granularity).AddDays(-1);

    private static int WeekdayOffset(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Sunday => 6,
        _ => (int)date.DayOfWeek - 1
    };

    private static string PeriodLabel(DateOnly start, string granularity) => granularity switch
    {
        "day" => start.ToString("yyyy-MM-dd"),
        "week" => $"{start:yyyy-MM-dd} - {start.AddDays(6):yyyy-MM-dd}",
        "month" => start.ToString("yyyy-MM"),
        "quarter" => $"{start.Year}-Q{((start.Month - 1) / 3) + 1}",
        "year" => start.ToString("yyyy"),
        _ => throw new InvalidOperationException("The aggregation granularity was not normalized.")
    };

    private string EncodeKey(
        TransactionAggregationCriteria criteria,
        string dimensionValue,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        int recordCount,
        DateTimeOffset snapshotAt,
        string displayCurrencyCode)
    {
        var selection = new TransactionAggregationSelection(
            criteria.Dimension,
            dimensionValue,
            criteria.Dimension == "category" && criteria.RollupCategories,
            periodStart,
            periodEnd);
        return keyCodec.Encode(new TransactionDrillDownKeyPayload(
            1,
            criteria.UserId,
            snapshotAt,
            snapshotAt.Add(drillDownOptions.KeyLifetime),
            criteria.Dimension,
            criteria.Granularity,
            displayCurrencyCode,
            criteria.From,
            criteria.To,
            [.. criteria.Selections, selection],
            recordCount,
            new TransactionDrillDownFilters(
                criteria.FinancialAccountId,
                criteria.CreditCardId,
                criteria.CategoryId,
                criteria.TagId,
                criteria.CounterpartyId,
                criteria.Direction,
                criteria.MinimumAmount,
                criteria.MaximumAmount,
                criteria.Text)));
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private sealed record BucketIdentity(string DimensionValue, string Label, DateOnly? BucketStart);

}
