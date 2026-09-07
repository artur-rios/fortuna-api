using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class QueryRecordsAsTableQuery : BaseQuery
{
    public string RecordSet { get; set; } = string.Empty;
    public IReadOnlyCollection<string> Columns { get; set; } = [];
    public IReadOnlyCollection<TableFilterInput> Filters { get; set; } = [];
    public IReadOnlyCollection<TableSortInput> Sorts { get; set; } = [];
    public string? DisplayCurrencyCode { get; set; }
}

public sealed class TableFilterInput
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "eq";
    public string Value { get; set; } = string.Empty;
}

public sealed class TableSortInput
{
    public string Field { get; set; } = string.Empty;
    public bool Descending { get; set; }
}
