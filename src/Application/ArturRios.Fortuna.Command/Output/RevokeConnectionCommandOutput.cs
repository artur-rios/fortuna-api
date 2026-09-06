using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class RevokeConnectionCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public TransactionSourceType DataSourceType { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public ConnectionStatus Status { get; set; }
    public bool ImportedDataRetained { get; set; }
    public int StoppedSynchronizations { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
