using System.Text.Json.Serialization;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public enum DataExportDelivery
{
    Direct = 1,
    Queued = 2
}

public sealed class RequestDataExportCommandOutput : CommandOutput
{
    public DataExportDelivery Delivery { get; set; }
    public Guid? ExportId { get; set; }
    public Guid? JobId { get; set; }
    public DataExportFormat Format { get; set; }
    public int RowCount { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    [JsonIgnore]
    public byte[] Content { get; set; } = [];
}
