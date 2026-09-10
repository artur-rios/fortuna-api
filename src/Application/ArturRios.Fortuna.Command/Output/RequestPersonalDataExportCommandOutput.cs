using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RequestPersonalDataExportCommandOutput : CommandOutput
{
    public Guid JobId { get; init; }
    public DataExportStatus Status { get; init; }
    public int Progress { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
