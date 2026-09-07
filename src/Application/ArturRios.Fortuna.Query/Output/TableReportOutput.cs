using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class TableReportOutput : QueryOutput
{
    public string RecordSet { get; set; } = string.Empty;
    public IReadOnlyCollection<TableColumnOutput> Columns { get; set; } = [];
    public IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyCollection<TableTotalOutput> Totals { get; set; } = [];
}

public sealed class TableColumnOutput
{
    public string Name { get; set; } = string.Empty;
    public TableColumnType Type { get; set; }
    public bool IsNumeric { get; set; }
    public string? CurrencyColumn { get; set; }
}

public sealed class TableTotalOutput
{
    public string Column { get; set; } = string.Empty;
    public string? CurrencyCode { get; set; }
    public decimal? Value { get; set; }
    public bool IsFullyConverted { get; set; } = true;
    public IReadOnlyCollection<TableTotalConversionOutput> Conversions { get; set; } = [];
}

public sealed class TableTotalConversionOutput
{
    public string? SourceCurrencyCode { get; set; }
    public decimal SourceValue { get; set; }
    public DateOnly? FigureDate { get; set; }
    public decimal? ConvertedValue { get; set; }
    public decimal? AppliedRate { get; set; }
    public DateOnly? RateDate { get; set; }
    public ExchangeRateSource? RateSource { get; set; }
    public string? UnconvertedReason { get; set; }
}
