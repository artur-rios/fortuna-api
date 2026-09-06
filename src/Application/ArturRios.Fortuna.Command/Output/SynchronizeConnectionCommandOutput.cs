using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class SynchronizeConnectionCommandOutput : CommandOutput
{
    public Guid ImportJobId { get; set; }
    public ImportJobStatus Status { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
}
