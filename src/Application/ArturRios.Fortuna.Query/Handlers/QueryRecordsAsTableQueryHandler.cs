using ArturRios.Fortuna.Query.Conversion;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class QueryRecordsAsTableQueryHandler(
    ICurrentProfileResolver profileResolver,
    ITableReportReader reports,
    ICurrencyReader currencies,
    IExchangeRateReader rates,
    PaginationOptions paginationOptions,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<QueryRecordsAsTableQuery, TableReportOutput>
{
    public async Task<DataOutput<TableReportOutput?>> HandleAsync(QueryRecordsAsTableQuery query)
    {
        var output = DataOutput<TableReportOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(TableReportMessages.ProfileNotFound);
        }

        var displayCode = DisplayCurrency.ResolveCode(query.DisplayCurrencyCode, profile);
        var displayCurrency = await currencies.FindByCodeAsync(displayCode, CancellationToken.None);
        if (displayCurrency is null)
        {
            return output
                .WithError(TableReportMessages.DisplayCurrencyUnsupported)
                .WithMessage(TableReportMessages.UnknownCurrency(displayCode));
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

        var converter = new FigureConverter(rates, displayCurrency);
        var totals = await ConvertTotalsAsync(result.Report.TotalGroups, converter);

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
                Totals = totals,
                MissingRates = MissingExchangeRateOutput.From(converter)
            })
            .WithMessage(TableReportMessages.RetrievedSuccessfully);
    }

    private async Task<IReadOnlyCollection<TableTotalOutput>> ConvertTotalsAsync(
        IReadOnlyCollection<TableTotalGroupSnapshot> groups,
        FigureConverter converter)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var totals = new List<TableTotalOutput>();
        foreach (var column in groups.GroupBy(group => group.Column))
        {
            var ordered = column
                .OrderBy(item => item.CurrencyCode, StringComparer.Ordinal)
                .ThenBy(item => item.FigureDate)
                .ToArray();
            if (ordered.All(group => group.CurrencyCode is null))
            {
                // Counts and other non-monetary figures are neither converted nor rounded.
                totals.Add(new TableTotalOutput
                {
                    Column = column.Key,
                    Value = ordered.Sum(group => group.Value),
                    Conversions = ordered.Select(group => new TableTotalConversionOutput
                    {
                        SourceValue = group.Value,
                        FigureDate = group.FigureDate,
                        ConvertedValue = group.Value
                    }).ToArray()
                });
                continue;
            }

            // Historical rows convert at their own date; undated rows at today's rate.
            var conversions = new List<FigureConversion>(ordered.Length);
            foreach (var group in ordered)
            {
                conversions.Add(await converter.ConvertAsync(
                    group.CurrencyCode ?? converter.DisplayCurrency.Code,
                    group.Value,
                    group.FigureDate ?? today));
            }

            var total = converter.Total(conversions);
            totals.Add(new TableTotalOutput
            {
                Column = column.Key,
                CurrencyCode = converter.DisplayCurrency.Code,
                Value = total,
                IsFullyConverted = total.HasValue,
                Conversions = ordered.Zip(conversions, (group, conversion) =>
                    new TableTotalConversionOutput
                    {
                        SourceCurrencyCode = group.CurrencyCode,
                        SourceValue = group.Value,
                        FigureDate = group.FigureDate,
                        ConvertedValue = converter.Round(conversion.Value),
                        AppliedRate = conversion.Rate?.Rate,
                        RateDate = conversion.Rate?.RateDate,
                        RateSource = conversion.Rate?.Source,
                        UnconvertedReason = conversion.UnconvertedReason
                    }).ToArray()
            });
        }

        return totals;
    }

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
        _ => TableReportMessages.ReadFailed
    };
}
