using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class ProcessingConsentQueryOutput : QueryOutput
{
    public IReadOnlyCollection<ProcessingConsentStateOutput> Consents { get; set; } = [];
}

public sealed class ProcessingConsentStateOutput
{
    public string Purpose { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string? GrantedVersion { get; set; }
    public DateTimeOffset? GrantedAt { get; set; }
    public bool IsCurrent { get; set; }
}
