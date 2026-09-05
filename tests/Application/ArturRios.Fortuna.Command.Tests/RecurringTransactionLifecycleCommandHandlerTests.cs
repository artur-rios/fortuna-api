using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecurringTransactionLifecycleCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedRule_WhenDeleted_ThenPastOccurrencesRemainUnchanged()
    {
        var id = Guid.NewGuid();
        var store = new StubLifecycle(new(id, RecurringTransactionLifecycleOutcome.Succeeded));

        var result = await Handler(Profile(), store).HandleAsync(
            new DeleteRecurringTransactionCommand { Id = id });

        Assert.True(result.Success);
        Assert.Equal(id, result.Data?.Id);
        Assert.False(result.Data?.MaterializedOccurrencesChanged);
    }

    [UnitFact]
    public async Task GivenUnknownRule_WhenDeleted_ThenNotFoundReturns()
    {
        var result = await Handler(Profile(), new StubLifecycle(new(
            null, RecurringTransactionLifecycleOutcome.NotFound))).HandleAsync(
            new DeleteRecurringTransactionCommand { Id = Guid.NewGuid() });

        Assert.Contains(RecurringTransactionMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenDeleted_ThenStoreIsNotCalled()
    {
        var store = new StubLifecycle(new(
            Guid.NewGuid(), RecurringTransactionLifecycleOutcome.Succeeded));

        var result = await Handler(null, store).HandleAsync(
            new DeleteRecurringTransactionCommand { Id = Guid.NewGuid() });

        Assert.Contains(RecurringTransactionMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Id);
    }

    private static DeleteRecurringTransactionCommandHandler Handler(
        UserProfileSnapshot? profile,
        IRecurringTransactionLifecycleStore store) => new(
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfiles(profile),
        store,
        new FixedTimeProvider(Now));

    private sealed class StubLifecycle(RecurringTransactionLifecycleResult result)
        : IRecurringTransactionLifecycleStore
    {
        public Guid? Id { get; private set; }

        public Task<RecurringTransactionLifecycleResult> SoftDeleteAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Id = id;
            return Task.FromResult(result);
        }
    }

    private sealed class StubProfiles(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(Guid id, CancellationToken token) =>
            Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(Guid id, CancellationToken token) =>
            Task.FromResult(profile);
    }

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static UserProfileSnapshot Profile() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);
}
