using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Reporting;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DataExportBuilder(
    ITableReportReader reports,
    ICurrencyReader currencies,
    IExchangeRateReader rates)
{
    public async Task<DataExportBuildResult> BuildAsync(
        Guid userId,
        DataExportSpecification specification,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        var displayCurrency = string.IsNullOrWhiteSpace(specification.DisplayCurrencyCode)
            ? null
            : specification.DisplayCurrencyCode.Trim().ToUpperInvariant();
        var displayDefinition = displayCurrency is null
            ? null
            : await currencies.FindByCodeAsync(displayCurrency, cancellationToken);
        if (displayCurrency is not null && displayDefinition is null)
        {
            return DataExportBuildResult.Failed(TableReportMessages.DisplayCurrencyUnsupported);
        }

        var columns = specification.Columns.Select(column => column.Trim()).ToList();
        var result = await ReadAsync(userId, specification, columns, maximumRows,
            cancellationToken);
        if (result.Outcome != TableReportReadOutcome.Succeeded || result.Report is null)
        {
            return DataExportBuildResult.Failed(Error(result, specification.RecordSet.Trim()));
        }

        var currencyColumns = result.Report.Columns
            .Select(column => column.CurrencyColumn)
            .Where(column => column is not null)
            .Select(column => column!)
            .Where(column => !columns.Contains(column, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (currencyColumns.Length > 0)
        {
            columns.AddRange(currencyColumns);
            result = await ReadAsync(userId, specification, columns, maximumRows,
                cancellationToken);
            if (result.Outcome != TableReportReadOutcome.Succeeded || result.Report is null)
            {
                return DataExportBuildResult.Failed(
                    Error(result, specification.RecordSet.Trim()));
            }
        }

        var totals = await ConvertTotalsAsync(
            result.Report.TotalGroups,
            displayCurrency,
            displayDefinition?.MinorUnitDigits,
            cancellationToken);
        return DataExportBuildResult.Succeeded(new DataExportDocument(
            result.Report.RecordSet,
            result.Report.Columns,
            result.Report.Rows,
            totals,
            specification.Locale),
            result.Report.TotalCount);
    }

    private Task<TableReportReadResult> ReadAsync(
        Guid userId,
        DataExportSpecification specification,
        IReadOnlyCollection<string> columns,
        int maximumRows,
        CancellationToken cancellationToken) => reports.ReadAsync(new TableReportCriteria(
            userId,
            specification.RecordSet.Trim(),
            columns,
            specification.Filters.Select(filter => new TableFilterCriteria(
                filter.Field.Trim(), filter.Operator.Trim(), filter.Value)).ToArray(),
            specification.Sorts.Select(sort => new TableSortCriteria(
                sort.Field.Trim(), sort.Descending)).ToArray(),
            1,
            maximumRows),
        cancellationToken);

    private async Task<IReadOnlyCollection<DataExportTotal>> ConvertTotalsAsync(
        IReadOnlyCollection<TableTotalGroupSnapshot> groups,
        string? displayCurrency,
        short? displayCurrencyDigits,
        CancellationToken cancellationToken)
    {
        if (displayCurrency is null)
        {
            return groups
                .GroupBy(group => new { group.Column, group.CurrencyCode })
                .OrderBy(group => group.Key.Column, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Key.CurrencyCode, StringComparer.Ordinal)
                .Select(group => new DataExportTotal(
                    group.Key.Column,
                    group.Key.CurrencyCode,
                    group.Sum(item => item.Value),
                    true))
                .ToArray();
        }

        var totals = new List<DataExportTotal>();
        foreach (var column in groups.GroupBy(group => group.Column))
        {
            var converted = new List<decimal?>();
            foreach (var group in column)
            {
                if (group.CurrencyCode is null || group.CurrencyCode == displayCurrency)
                {
                    converted.Add(group.Value);
                    continue;
                }

                var rate = await rates.FindApplicableAsync(
                    group.CurrencyCode,
                    displayCurrency,
                    group.FigureDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    cancellationToken);
                converted.Add(rate is null ? null : group.Value * rate.Rate);
            }

            var fullyConverted = converted.All(value => value.HasValue);
            totals.Add(new DataExportTotal(
                column.Key,
                column.Any(group => group.CurrencyCode is not null) ? displayCurrency : null,
                fullyConverted
                    ? decimal.Round(converted.Sum(value => value!.Value),
                        displayCurrencyDigits!.Value,
                        MidpointRounding.AwayFromZero)
                    : null,
                fullyConverted));
        }

        return totals;
    }

    private static string Error(TableReportReadResult result, string recordSet) =>
        result.Outcome switch
        {
            TableReportReadOutcome.RecordSetUnknown => TableReportMessages.UnknownRecordSet(
                result.InvalidName ?? recordSet,
                result.SupportedValues ?? []),
            TableReportReadOutcome.ColumnUnknown => TableReportMessages.UnknownColumn(
                recordSet, result.InvalidName ?? string.Empty),
            TableReportReadOutcome.FilterFieldUnknown => TableReportMessages.UnknownFilterField(
                recordSet, result.InvalidName ?? string.Empty),
            TableReportReadOutcome.FilterOperatorUnknown =>
                TableReportMessages.UnknownFilterOperator(
                    result.InvalidName ?? string.Empty,
                    result.InvalidOperator ?? string.Empty),
            TableReportReadOutcome.FilterValueInvalid => TableReportMessages.InvalidFilterValue(
                result.InvalidName ?? string.Empty,
                result.InvalidValue ?? string.Empty),
            TableReportReadOutcome.SortFieldUnknown => TableReportMessages.UnknownSortField(
                recordSet, result.InvalidName ?? string.Empty),
            _ => throw new InvalidOperationException("The table reader returned an invalid outcome.")
        };
}

public sealed record DataExportBuildResult(
    DataExportDocument? Document,
    int TotalCount,
    string? Error)
{
    public static DataExportBuildResult Succeeded(
        DataExportDocument document,
        int totalCount) => new(document, totalCount, null);

    public static DataExportBuildResult Failed(string error) => new(null, 0, error);
}
