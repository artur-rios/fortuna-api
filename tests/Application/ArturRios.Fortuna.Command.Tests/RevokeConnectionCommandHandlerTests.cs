using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RevokeConnectionCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedConnection_WhenRevoked_ThenRetainedDataIsReported()
    {
        var store = new StubStore(Result(ConnectionRevocationOutcome.Succeeded, 1));
        var handler = Handler(store);

        var result = await handler.HandleAsync(new RevokeConnectionCommand { Id = ConnectionId });

        Assert.True(result.Success);
        Assert.Equal(ConnectionStatus.Revoked, result.Data?.Status);
        Assert.True(result.Data?.ImportedDataRetained);
        Assert.Equal(1, result.Data?.StoppedSynchronizations);
        Assert.Contains(ConnectionMessages.RevokedSuccessfully, result.Messages);
        Assert.Equal(ConnectionId, store.Request?.ConnectionId);
        Assert.Equal(UserId, store.Request?.UserId);
    }

    [UnitFact]
    public async Task GivenAlreadyRevokedConnection_WhenRevoked_ThenRequestIsIdempotent()
    {
        var handler = Handler(new StubStore(Result(
            ConnectionRevocationOutcome.AlreadyRevoked, 0)));

        var result = await handler.HandleAsync(new RevokeConnectionCommand { Id = ConnectionId });

        Assert.True(result.Success);
        Assert.Contains(ConnectionMessages.AlreadyRevoked, result.Messages);
    }

    [UnitFact]
    public async Task GivenUnknownConnection_WhenRevoked_ThenNotFoundIsReturned()
    {
        var handler = Handler(new StubStore(new ConnectionRevocationResult(
            null, 0, ConnectionRevocationOutcome.NotFound)));

        var result = await handler.HandleAsync(new RevokeConnectionCommand { Id = ConnectionId });

        Assert.False(result.Success);
        Assert.Contains(ConnectionMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenRevoked_ThenNotFoundIsReturnedWithoutMutation()
    {
        var store = new StubStore(Result(ConnectionRevocationOutcome.Succeeded, 0));
        var handler = new RevokeConnectionCommandHandler(
            new StubActorAccessor(new RequestActor(UserId, 3, null, [])),
            new StubProfileReader(null),
            store,
            new FixedTimeProvider());

        var result = await handler.HandleAsync(new RevokeConnectionCommand { Id = ConnectionId });

        Assert.False(result.Success);
        Assert.Contains(ConnectionMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Request);
    }

    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ConnectionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static RevokeConnectionCommandHandler Handler(StubStore store) => new(
        new StubActorAccessor(new RequestActor(UserId, 3, null, []) { IsLocal = true }),
        new StubProfileReader(new UserProfileSnapshot(
            UserId, null, "Owner", "BRL", false, Now, Now)),
        store,
        new FixedTimeProvider());

    private static ConnectionRevocationResult Result(
        ConnectionRevocationOutcome outcome,
        int stopped) => new(
            new ConnectionSnapshot(
                ConnectionId,
                TransactionSourceType.Pluggy,
                "item",
                ConnectionStatus.Revoked,
                Now.AddDays(-1),
                Now),
            stopped,
            outcome);

    private sealed class StubStore(ConnectionRevocationResult result)
        : IConnectionRevocationStore
    {
        public ConnectionRevocation? Request { get; private set; }

        public Task<ConnectionRevocationResult> RevokeAsync(
            ConnectionRevocation revocation,
            CancellationToken cancellationToken)
        {
            Request = revocation;
            return Task.FromResult(result);
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
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
