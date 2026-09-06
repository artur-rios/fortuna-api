using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class ImportPdfInvoiceCommandOutput : CommandOutput
{
    public Guid ImportJobId { get; set; }
    public ImportJobStatus Status { get; set; }
}
