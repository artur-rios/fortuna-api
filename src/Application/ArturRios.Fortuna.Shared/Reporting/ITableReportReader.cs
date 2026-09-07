namespace ArturRios.Fortuna.Shared.Reporting;

public interface ITableReportReader
{
    Task<TableReportReadResult> ReadAsync(
        TableReportCriteria criteria,
        CancellationToken cancellationToken);
}

public sealed record TableReportCriteria(
    Guid UserId,
    string RecordSet,
    IReadOnlyCollection<string> Columns,
    IReadOnlyCollection<TableFilterCriteria> Filters,
    IReadOnlyCollection<TableSortCriteria> Sorts,
    int PageNumber,
    int PageSize);

public sealed record TableFilterCriteria(string Field, string Operator, string Value);

public sealed record TableSortCriteria(string Field, bool Descending);

public enum TableColumnType
{
    Uuid = 1,
    Text = 2,
    Integer = 3,
    Decimal = 4,
    Boolean = 5,
    Date = 6,
    Timestamp = 7,
    Enumeration = 8
}

public sealed record TableColumnSnapshot(
    string Name,
    TableColumnType Type,
    bool IsNumeric,
    string? CurrencyColumn);

public sealed record TableTotalGroupSnapshot(
    string Column,
    string? CurrencyCode,
    DateOnly? FigureDate,
    decimal Value);

public sealed record TableReportSnapshot(
    string RecordSet,
    IReadOnlyCollection<TableColumnSnapshot> Columns,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows,
    int TotalCount,
    int PageNumber,
    int PageSize,
    IReadOnlyCollection<TableTotalGroupSnapshot> TotalGroups);

public enum TableReportReadOutcome
{
    Succeeded = 1,
    RecordSetUnknown = 2,
    ColumnUnknown = 3,
    FilterFieldUnknown = 4,
    FilterOperatorUnknown = 5,
    FilterValueInvalid = 6,
    SortFieldUnknown = 7
}

public sealed record TableReportReadResult(
    TableReportReadOutcome Outcome,
    TableReportSnapshot? Report = null,
    string? InvalidName = null,
    string? InvalidOperator = null,
    string? InvalidValue = null,
    IReadOnlyCollection<string>? SupportedValues = null);
