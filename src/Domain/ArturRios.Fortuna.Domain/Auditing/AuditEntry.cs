using ArturRios.Fortuna.Domain.Users;

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
        UserProfile? user,
        string operation,
        string? entityType,
        Guid? entityPublicId,
        AuditOutcome outcome,
        string? reason,
        DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(operation) || operation.Length > 150)
        {
            throw new ArgumentException("An operation between 1 and 150 characters is required.", nameof(operation));
        }

        if (entityType?.Length > 100)
        {
            throw new ArgumentException("An entity type cannot exceed 100 characters.", nameof(entityType));
        }

        if (reason?.Length > 1000)
        {
            throw new ArgumentException("An audit reason cannot exceed 1000 characters.", nameof(reason));
        }

        User = user;
        UserId = user?.Id;
        Operation = operation;
        EntityType = entityType;
        EntityPublicId = entityPublicId;
        Outcome = outcome;
        Reason = reason;
        OccurredAt = occurredAt;
    }

    public long Id { get; private set; }
    public long? UserId { get; private set; }
    public UserProfile? User { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string? EntityType { get; private set; }
    public Guid? EntityPublicId { get; private set; }
    public AuditOutcome Outcome { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
