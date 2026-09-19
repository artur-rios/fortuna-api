using ArturRios.Fortuna.Domain.Guards;

namespace ArturRios.Fortuna.Domain.Auditing;

public enum AuditOutcome : short
{
    Succeeded = 1,
    Refused = 2
}

/// <summary>One append-only record of an attempted write.</summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    public AuditEntry(
        Guid? subjectReference,
        string operation,
        string? entityType,
        Guid? entityPublicId,
        AuditOutcome outcome,
        string? reason,
        DateTimeOffset occurredAt)
    {
        SubjectReference = subjectReference;
        Operation = BoundedText.Required(
            operation,
            150,
            nameof(operation),
            "An operation between 1 and 150 characters is required.");
        EntityType = BoundedText.Optional(
            entityType,
            100,
            nameof(entityType),
            "An entity type cannot exceed 100 characters.");
        EntityPublicId = entityPublicId;
        Outcome = outcome;
        Reason = BoundedText.Optional(
            reason,
            1000,
            nameof(reason),
            "An audit reason cannot exceed 1000 characters.");
        OccurredAt = occurredAt;
    }

    public long Id { get; private set; }
    public Guid? SubjectReference { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string? EntityType { get; private set; }
    public Guid? EntityPublicId { get; private set; }
    public AuditOutcome Outcome { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
