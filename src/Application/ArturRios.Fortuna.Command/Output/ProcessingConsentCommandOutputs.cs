using ArturRios.Mediator.Command;

namespace ArturRios.Fortuna.Command.Output;

public sealed class GrantProcessingConsentCommandOutput : CommandOutput
{
    public Guid Id { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset GrantedAt { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class WithdrawProcessingConsentCommandOutput : CommandOutput
{
    public string Purpose { get; set; } = string.Empty;
    public int RevokedConnections { get; set; }
    public int StoppedSynchronizations { get; set; }
}
