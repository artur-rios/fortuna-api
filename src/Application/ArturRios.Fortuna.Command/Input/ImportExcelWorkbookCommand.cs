using System.Text.Json.Serialization;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Input;

public sealed class ImportExcelWorkbookCommand : BaseCommand
{
    [JsonIgnore]
    public string? CorrelationId { get; set; }

    public Guid TargetId { get; set; }
    public ImportTargetType TargetType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public ExcelColumnMapping Mapping { get; set; } = new("", "", "", null, null, null);
    public bool CreateMissingCategories { get; set; }
}
