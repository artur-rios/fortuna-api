using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Output;

public sealed class AuditEntryOutput : QueryOutput
{
    public Guid ActorUserId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public AuditOutcome Outcome { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
