using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CloseCreditCardStatementCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 21, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedStatement_WhenClosed_ThenFixedTotalIsReturned()
    {
        var profile = Profile();
        var statement = Snapshot();
        var store = new StubStore(new(statement, CreditCardStatementCloseOutcome.Succeeded));

        var result = await Handler(profile, store).HandleAsync(
            new CloseCreditCardStatementCommand { Id = statement.Id });

        Assert.True(result.Success);
        Assert.Equal(statement.Id, result.Data?.Id);
        Assert.Equal(125m, result.Data?.AmountDue);
        Assert.True(store.ExplicitRequest);
        Assert.Equal(new DateOnly(2026, 9, 4), store.AsOf);
        Assert.Contains(CreditCardStatementMessages.ClosedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(CreditCardStatementCloseOutcome.NotFound,
        CreditCardStatementMessages.NotFound)]
    [InlineData(CreditCardStatementCloseOutcome.SettledStatementFrozen,
        CreditCardStatementMessages.SettledStatementFrozen)]
    public async Task GivenCloseRefusal_WhenHandled_ThenExpectedErrorIsReturned(
        CreditCardStatementCloseOutcome outcome,
        string error)
    {
        var result = await Handler(Profile(), new StubStore(new(null, outcome)))
            .HandleAsync(new CloseCreditCardStatementCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Contains(error, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenClosed_ThenStoreIsNotCalled()
    {
        var store = new StubStore(new(Snapshot(), CreditCardStatementCloseOutcome.Succeeded));

        var result = await Handler(null, store).HandleAsync(new CloseCreditCardStatementCommand());

        Assert.False(result.Success);
        Assert.Contains(CreditCardStatementMessages.ProfileNotFound, result.Errors);
        Assert.False(store.Called);
    }

    private static CloseCreditCardStatementCommandHandler Handler(
        UserProfileSnapshot? profile,
        ICreditCardStatementCloser store) => new(
        new ActorAccessor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new Profiles(profile),
        store,
        new FixedTimeProvider(Now));

    private static CreditCardStatementSnapshot Snapshot() => new(
        Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 21),
        new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 20),
        new DateOnly(2026, 10, 5), "Closed", 125m, 125m);

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private sealed class StubStore(CreditCardStatementCloseResult result)
        : ICreditCardStatementCloser
    {
        public bool Called { get; private set; }
        public bool ExplicitRequest { get; private set; }
        public DateOnly AsOf { get; private set; }

        public Task<CreditCardStatementCloseResult> CloseAsync(
            Guid userId,
            Guid statementId,
            DateOnly asOf,
            bool explicitRequest,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken)
        {
            Called = true;
            ExplicitRequest = explicitRequest;
            AsOf = asOf;
            return Task.FromResult(result);
        }
    }

    private sealed class Profiles(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class ActorAccessor(RequestActor actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
