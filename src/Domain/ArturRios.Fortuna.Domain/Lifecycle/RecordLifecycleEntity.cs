using System.ComponentModel.DataAnnotations.Schema;

namespace ArturRios.Fortuna.Domain.Lifecycle;

public enum RecordLifecycleConflict
{
    RestoreRequiresSoftDeletion = 1,
    HardDeleteRequiresSoftDeletion = 2,
    HardDeleteHasLiveReferences = 3
}

public sealed class RecordLifecycleConflictException(
    RecordLifecycleConflict conflict,
    IReadOnlyCollection<string>? liveReferences = null) : InvalidOperationException
{
    public RecordLifecycleConflict Conflict { get; } = conflict;
    public IReadOnlyCollection<string> LiveReferences { get; } = liveReferences ?? [];
}

public sealed record SoftDeletionResult(Guid CascadeId, bool Changed);

[NotMapped]
public abstract class RecordLifecycleEntity
{
    protected RecordLifecycleEntity()
    {
    }

    protected RecordLifecycleEntity(DateTimeOffset createdAt)
    {
        PublicId = Guid.NewGuid();
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid PublicId { get; private set; }
    public bool IsDeleted { get; private set; }
    public Guid? DeletionCascadeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public SoftDeletionResult SoftDelete(DateTimeOffset deletedAt)
    {
        if (IsDeleted)
        {
            return new SoftDeletionResult(DeletionCascadeId!.Value, false);
        }

        var cascadeId = Guid.NewGuid();
        MarkDeleted(cascadeId, deletedAt);
        return new SoftDeletionResult(cascadeId, true);
    }

    public bool SoftDeleteFromCascade(Guid cascadeId, DateTimeOffset deletedAt)
    {
        if (cascadeId == Guid.Empty)
        {
            throw new ArgumentException("A deletion cascade identifier is required.", nameof(cascadeId));
        }

        if (IsDeleted)
        {
            return false;
        }

        MarkDeleted(cascadeId, deletedAt);
        return true;
    }

    public Guid Restore(DateTimeOffset restoredAt)
    {
        if (!IsDeleted)
        {
            throw new RecordLifecycleConflictException(
                RecordLifecycleConflict.RestoreRequiresSoftDeletion);
        }

        var cascadeId = DeletionCascadeId!.Value;
        MarkRestored(restoredAt);
        return cascadeId;
    }

    public bool RestoreFromCascade(Guid cascadeId, DateTimeOffset restoredAt)
    {
        if (!IsDeleted || DeletionCascadeId != cascadeId)
        {
            return false;
        }

        MarkRestored(restoredAt);
        return true;
    }

    public void EnsureHardDeletionAllowed(IReadOnlyCollection<string>? liveReferences = null)
    {
        if (!IsDeleted)
        {
            throw new RecordLifecycleConflictException(
                RecordLifecycleConflict.HardDeleteRequiresSoftDeletion);
        }

        var references = liveReferences?
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray() ?? [];
        if (references.Length > 0)
        {
            throw new RecordLifecycleConflictException(
                RecordLifecycleConflict.HardDeleteHasLiveReferences,
                references);
        }
    }

    protected void MarkUpdated(DateTimeOffset updatedAt) => UpdatedAt = updatedAt;

    private void MarkDeleted(Guid cascadeId, DateTimeOffset deletedAt)
    {
        IsDeleted = true;
        DeletionCascadeId = cascadeId;
        UpdatedAt = deletedAt;
    }

    private void MarkRestored(DateTimeOffset restoredAt)
    {
        IsDeleted = false;
        DeletionCascadeId = null;
        UpdatedAt = restoredAt;
    }
}
