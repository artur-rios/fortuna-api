using System.Text.Json.Serialization;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class RequestDataExportCommand : BaseCommand
{
    [JsonIgnore]
    public string? CorrelationId { get; set; }

    public string RecordSet { get; set; } = string.Empty;
    public IReadOnlyCollection<string> Columns { get; set; } = [];
    public IReadOnlyCollection<DataExportFilterInput> Filters { get; set; } = [];
    public IReadOnlyCollection<DataExportSortInput> Sorts { get; set; } = [];
    public string? DisplayCurrencyCode { get; set; }
    public string Format { get; set; } = string.Empty;
    public string? Locale { get; set; }
}

public sealed class DataExportFilterInput
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "eq";
    public string Value { get; set; } = string.Empty;
}

public sealed class DataExportSortInput
{
    public string Field { get; set; } = string.Empty;
    public bool Descending { get; set; }
}
