using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Moq;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class AttachmentLifecycleCommandHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AttachmentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 2, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedAttachment_WhenSoftDeleted_ThenDeletedSnapshotIsReturned()
    {
        var store = new Mock<IAttachmentLifecycleStore>();
        store.Setup(item => item.SoftDeleteAsync(UserId, AttachmentId, Now, CancellationToken.None))
            .ReturnsAsync(new AttachmentLifecycleResult(
                AttachmentLifecycleOutcome.Succeeded,
                AttachmentId,
                true));

        var result = await DeleteHandler(store.Object).HandleAsync(
            new DeleteAttachmentCommand { Id = AttachmentId });

        Assert.True(result.Success);
        Assert.Equal(AttachmentId, result.Data?.Id);
        Assert.True(result.Data?.IsDeleted);
        Assert.Contains(AttachmentMessages.DeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenSoftDeletedAttachment_WhenHardDeleted_ThenRemovalIsReturned()
    {
        var store = new Mock<IAttachmentLifecycleStore>();
        store.Setup(item => item.HardDeleteAsync(UserId, AttachmentId, CancellationToken.None))
            .ReturnsAsync(new AttachmentLifecycleResult(
                AttachmentLifecycleOutcome.Succeeded,
                AttachmentId,
                false));

        var result = await HardDeleteHandler(store.Object).HandleAsync(
            new HardDeleteAttachmentCommand { Id = AttachmentId });

        Assert.True(result.Success);
        Assert.False(result.Data?.IsDeleted);
        Assert.Contains(AttachmentMessages.HardDeletedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(AttachmentLifecycleOutcome.NotFound, AttachmentMessages.AttachmentNotFound)]
    [InlineData(AttachmentLifecycleOutcome.HardDeleteRequiresSoftDeletion,
        AttachmentMessages.HardDeleteRequiresSoftDeletion)]
    [InlineData(AttachmentLifecycleOutcome.StorageUnavailable,
        AttachmentMessages.StorageUnavailable)]
    public async Task GivenHardDeleteRefusal_WhenHandled_ThenExpectedErrorIsReturned(
        AttachmentLifecycleOutcome outcome,
        string expected)
    {
        var store = new Mock<IAttachmentLifecycleStore>();
        store.Setup(item => item.HardDeleteAsync(UserId, AttachmentId, CancellationToken.None))
            .ReturnsAsync(new AttachmentLifecycleResult(outcome));

        var result = await HardDeleteHandler(store.Object).HandleAsync(
            new HardDeleteAttachmentCommand { Id = AttachmentId });

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenDeleted_ThenStoreIsNotCalled()
    {
        var store = new Mock<IAttachmentLifecycleStore>();
        var handler = new DeleteAttachmentCommandHandler(
            new StubActorAccessor(new RequestActor(UserId, 3, null, []) { IsLocal = true }),
            new StubProfileReader(null),
            store.Object,
            new FixedTimeProvider());

        var result = await handler.HandleAsync(new DeleteAttachmentCommand { Id = AttachmentId });

        Assert.Contains(AttachmentMessages.ProfileNotFound, result.Errors);
        store.Verify(item => item.SoftDeleteAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static DeleteAttachmentCommandHandler DeleteHandler(
        IAttachmentLifecycleStore store) => new(
        Actor(),
        Profiles(),
        store,
        new FixedTimeProvider());

    private static HardDeleteAttachmentCommandHandler HardDeleteHandler(
        IAttachmentLifecycleStore store) => new(
        Actor(),
        Profiles(),
        store);

    private static StubActorAccessor Actor() =>
        new(new RequestActor(UserId, 3, null, []) { IsLocal = true });

    private static StubProfileReader Profiles() => new(new UserProfileSnapshot(
        UserId,
        null,
        "Owner",
        "BRL",
        false,
        Now,
        Now));

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject, CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId, CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
