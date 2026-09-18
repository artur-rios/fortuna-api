using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class RecordLifecycleTests
{
    [UnitFact]
    public void GivenNewRecord_WhenCreated_ThenItIsLiveWithLifecycleTimestamps()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var record = new TestRecord(createdAt);

        Assert.NotEqual(Guid.Empty, record.PublicId);
        Assert.False(record.IsDeleted);
        Assert.Null(record.DeletionCascadeId);
        Assert.Equal(createdAt, record.CreatedAt);
        Assert.Equal(createdAt, record.UpdatedAt);
    }

    [UnitFact]
    public void GivenLiveRecord_WhenSoftDeleted_ThenCascadeAndTimestampAreRecorded()
    {
        var record = new TestRecord(DateTimeOffset.UtcNow.AddDays(-1));
        var deletedAt = DateTimeOffset.UtcNow;

        var deletion = record.SoftDelete(deletedAt);

        Assert.True(deletion.Changed);
        Assert.NotEqual(Guid.Empty, deletion.CascadeId);
        Assert.True(record.IsDeleted);
        Assert.Equal(deletion.CascadeId, record.DeletionCascadeId);
        Assert.Equal(deletedAt, record.UpdatedAt);
    }

    [UnitFact]
    public void GivenAlreadyDeletedRecord_WhenSoftDeletedAgain_ThenOperationIsIdempotent()
    {
        var record = new TestRecord(DateTimeOffset.UtcNow);
        var first = record.SoftDelete(DateTimeOffset.UtcNow.AddMinutes(1));

        var second = record.SoftDelete(DateTimeOffset.UtcNow.AddMinutes(2));

        Assert.False(second.Changed);
        Assert.Equal(first.CascadeId, second.CascadeId);
    }

    [UnitFact]
    public void GivenDependentDeletedByParent_WhenParentRestores_ThenDependentRestoresWithSameCascade()
    {
        var parent = new TestRecord(DateTimeOffset.UtcNow);
        var dependent = new TestRecord(DateTimeOffset.UtcNow);
        var deletion = parent.SoftDelete(DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.True(dependent.SoftDeleteFromCascade(deletion.CascadeId, DateTimeOffset.UtcNow.AddMinutes(1)));

        var restoredCascade = parent.Restore(DateTimeOffset.UtcNow.AddMinutes(2));
        var restored = dependent.RestoreFromCascade(restoredCascade, DateTimeOffset.UtcNow.AddMinutes(2));

        Assert.True(restored);
        Assert.False(parent.IsDeleted);
        Assert.False(dependent.IsDeleted);
        Assert.Null(dependent.DeletionCascadeId);
    }

    [UnitFact]
    public void GivenDependentDeletedBeforeParent_WhenParentRestores_ThenDependentStaysDeleted()
    {
        var parent = new TestRecord(DateTimeOffset.UtcNow);
        var dependent = new TestRecord(DateTimeOffset.UtcNow);
        var dependentDeletion = dependent.SoftDelete(DateTimeOffset.UtcNow.AddMinutes(1));
        var parentDeletion = parent.SoftDelete(DateTimeOffset.UtcNow.AddMinutes(2));
        Assert.False(dependent.SoftDeleteFromCascade(parentDeletion.CascadeId, DateTimeOffset.UtcNow.AddMinutes(2)));

        var restoredCascade = parent.Restore(DateTimeOffset.UtcNow.AddMinutes(3));
        var restored = dependent.RestoreFromCascade(restoredCascade, DateTimeOffset.UtcNow.AddMinutes(3));

        Assert.False(restored);
        Assert.True(dependent.IsDeleted);
        Assert.Equal(dependentDeletion.CascadeId, dependent.DeletionCascadeId);
    }

    [UnitFact]
    public void GivenLiveRecord_WhenRestoreIsRequested_ThenConflictIsRaised()
    {
        var exception = Assert.Throws<RecordLifecycleConflictException>(() =>
            new TestRecord(DateTimeOffset.UtcNow).Restore(DateTimeOffset.UtcNow));

        Assert.Equal(RecordLifecycleConflict.RestoreRequiresSoftDeletion, exception.Conflict);
    }

    [UnitFact]
    public void GivenLiveRecord_WhenHardDeleteIsChecked_ThenSoftDeletionIsRequired()
    {
        var check = new TestRecord(DateTimeOffset.UtcNow).CheckHardDeletion();

        Assert.False(check.IsAllowed);
        Assert.Equal(RecordLifecycleConflict.HardDeleteRequiresSoftDeletion, check.Conflict);
    }

    [UnitFact]
    public void GivenSoftDeletedRecordWithLiveReferences_WhenHardDeleteIsChecked_ThenReferencesAreNamed()
    {
        var record = new TestRecord(DateTimeOffset.UtcNow);
        record.SoftDelete(DateTimeOffset.UtcNow.AddMinutes(1));

        var check = record.CheckHardDeletion(["transactions", "goals", "transactions"]);

        Assert.False(check.IsAllowed);
        Assert.Equal(RecordLifecycleConflict.HardDeleteHasLiveReferences, check.Conflict);
        Assert.Equal(["goals", "transactions"], check.LiveReferences);
    }

    [UnitFact]
    public void GivenSoftDeletedUnreferencedRecord_WhenHardDeleteIsChecked_ThenItIsAllowed()
    {
        var record = new TestRecord(DateTimeOffset.UtcNow);
        record.SoftDelete(DateTimeOffset.UtcNow.AddMinutes(1));

        var check = record.CheckHardDeletion();

        Assert.True(check.IsAllowed);
        Assert.Null(check.Conflict);
        Assert.Empty(check.LiveReferences);
    }

    private sealed class TestRecord(DateTimeOffset createdAt) : RecordLifecycleEntity(createdAt);
}
