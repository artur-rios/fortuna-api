using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class QueryRecordsAsTableQueryHandler(
    IValidator<QueryRecordsAsTableQuery> validator,
    IUserProfileReader profiles,
    ITableReportReader reports,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    IRequestActorAccessor actorAccessor,
    PaginationOptions paginationOptions)
    : IQueryHandlerAsync<QueryRecordsAsTableQuery, TableReportOutput>
{
    public async Task<DataOutput<TableReportOutput?>> HandleAsync(QueryRecordsAsTableQuery query)
    {
        var output = DataOutput<TableReportOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(TableReportMessages.ProfileNotFound);
        }

        var displayCurrency = string.IsNullOrWhiteSpace(query.DisplayCurrencyCode)
            ? null
            : query.DisplayCurrencyCode.Trim().ToUpperInvariant();
        var displayCurrencyDefinition = displayCurrency is null
            ? null
            : await currencies.FindByCodeAsync(displayCurrency, CancellationToken.None);
        if (displayCurrency is not null && displayCurrencyDefinition is null)
        {
            return output
                .WithError(TableReportMessages.DisplayCurrencyUnsupported)
                .WithMessage(TableReportMessages.UnknownCurrency(displayCurrency));
        }

        var result = await reports.ReadAsync(new TableReportCriteria(
            profile.Id,
            query.RecordSet.Trim(),
            query.Columns.Select(column => column.Trim()).ToArray(),
            query.Filters.Select(filter => new TableFilterCriteria(
                filter.Field.Trim(),
                filter.Operator.Trim(),
                filter.Value)).ToArray(),
            query.Sorts.Select(sort => new TableSortCriteria(
                sort.Field.Trim(),
                sort.Descending)).ToArray(),
            query.PageNumber,
            Math.Min(query.PageSize, paginationOptions.MaximumPageSize)),
            CancellationToken.None);
        if (result.Outcome != TableReportReadOutcome.Succeeded || result.Report is null)
        {
            return output.WithError(Error(result, query.RecordSet.Trim()));
        }

        var totals = await ConvertTotalsAsync(
            result.Report.TotalGroups,
            displayCurrency,
            displayCurrencyDefinition?.MinorUnitDigits);
        return output
            .WithData(new TableReportOutput
            {
                RecordSet = result.Report.RecordSet,
                Columns = result.Report.Columns.Select(column => new TableColumnOutput
                {
                    Name = column.Name,
                    Type = column.Type,
                    IsNumeric = column.IsNumeric,
                    CurrencyColumn = column.CurrencyColumn
                }).ToArray(),
                Rows = result.Report.Rows,
                TotalCount = result.Report.TotalCount,
                PageNumber = result.Report.PageNumber,
                PageSize = result.Report.PageSize,
                Totals = totals
            })
            .WithMessage(TableReportMessages.RetrievedSuccessfully);
    }

    private async Task<IReadOnlyCollection<TableTotalOutput>> ConvertTotalsAsync(
        IReadOnlyCollection<TableTotalGroupSnapshot> groups,
        string? displayCurrency,
        short? displayCurrencyDigits)
    {
        if (displayCurrency is null)
        {
            return groups
                .GroupBy(group => new { group.Column, group.CurrencyCode })
                .OrderBy(group => group.Key.Column, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Key.CurrencyCode, StringComparer.Ordinal)
                .Select(group => new TableTotalOutput
                {
                    Column = group.Key.Column,
                    CurrencyCode = group.Key.CurrencyCode,
                    Value = group.Sum(item => item.Value)
                })
                .ToArray();
        }

        var totals = new List<TableTotalOutput>();
        foreach (var column in groups.GroupBy(group => group.Column))
        {
            var isMonetary = column.Any(group => group.CurrencyCode is not null);
            var conversions = new List<TableTotalConversionOutput>();
            foreach (var group in column.OrderBy(item => item.CurrencyCode)
                         .ThenBy(item => item.FigureDate))
            {
                if (group.CurrencyCode is null || group.CurrencyCode == displayCurrency)
                {
                    conversions.Add(new TableTotalConversionOutput
                    {
                        SourceCurrencyCode = group.CurrencyCode,
                        SourceValue = group.Value,
                        FigureDate = group.FigureDate,
                        ConvertedValue = group.Value
                    });
                    continue;
                }

                var rate = await rates.FindApplicableAsync(
                    group.CurrencyCode,
                    displayCurrency,
                    group.FigureDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    CancellationToken.None);
                conversions.Add(rate is null
                    ? new TableTotalConversionOutput
                    {
                        SourceCurrencyCode = group.CurrencyCode,
                        SourceValue = group.Value,
                        FigureDate = group.FigureDate,
                        UnconvertedReason = FigureConversionMessages.RateUnavailable
                    }
                    : new TableTotalConversionOutput
                    {
                        SourceCurrencyCode = group.CurrencyCode,
                        SourceValue = group.Value,
                        FigureDate = group.FigureDate,
                        ConvertedValue = group.Value * rate.Rate,
                        AppliedRate = rate.Rate,
                        RateDate = rate.RateDate,
                        RateSource = rate.Source
                    });
            }

            var fullyConverted = conversions.All(conversion => conversion.ConvertedValue.HasValue);
            totals.Add(new TableTotalOutput
            {
                Column = column.Key,
                CurrencyCode = isMonetary ? displayCurrency : null,
                Value = fullyConverted
                    ? decimal.Round(
                        conversions.Sum(item => item.ConvertedValue!.Value),
                        displayCurrencyDigits!.Value,
                        MidpointRounding.AwayFromZero)
                    : null,
                IsFullyConverted = fullyConverted,
                Conversions = conversions
            });
        }

        return totals;
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private static string Error(TableReportReadResult result, string recordSet) => result.Outcome switch
    {
        TableReportReadOutcome.RecordSetUnknown => TableReportMessages.UnknownRecordSet(
            result.InvalidName ?? recordSet,
            result.SupportedValues ?? []),
        TableReportReadOutcome.ColumnUnknown => TableReportMessages.UnknownColumn(
            recordSet,
            result.InvalidName ?? string.Empty),
        TableReportReadOutcome.FilterFieldUnknown => TableReportMessages.UnknownFilterField(
            recordSet,
            result.InvalidName ?? string.Empty),
        TableReportReadOutcome.FilterOperatorUnknown =>
            TableReportMessages.UnknownFilterOperator(
                result.InvalidName ?? string.Empty,
                result.InvalidOperator ?? string.Empty),
        TableReportReadOutcome.FilterValueInvalid => TableReportMessages.InvalidFilterValue(
            result.InvalidName ?? string.Empty,
            result.InvalidValue ?? string.Empty),
        TableReportReadOutcome.SortFieldUnknown => TableReportMessages.UnknownSortField(
            recordSet,
            result.InvalidName ?? string.Empty),
        _ => throw new InvalidOperationException("The table reader returned an invalid outcome.")
    };
}
