using ArturRios.Fortuna.Domain.Auditing;

namespace ArturRios.Fortuna.Shared.Auditing;

public interface IAuditEntryStore
{
    Task AppendAsync(AuditEntryWrite entry, CancellationToken cancellationToken);
}

public sealed record AuditEntryWrite(
    Guid? ActorSubjectId,
    bool ActorIsLocal,
    string Operation,
    string? EntityType,
    Guid? EntityPublicId,
    AuditOutcome Outcome,
    string? Reason,
    DateTimeOffset OccurredAt);
