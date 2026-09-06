using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class ReauthenticateConnectionCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public TransactionSourceType DataSourceType { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public string Institution { get; set; } = string.Empty;
    public ConnectionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
