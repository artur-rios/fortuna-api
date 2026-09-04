using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class SettleCreditCardStatementCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 22, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidPayment_WhenSettled_ThenTransferAndBalanceAreReturned()
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var store = new StubStore(new(
            snapshot,
            CreditCardStatementSettlementOutcome.Succeeded));
        var command = Command();

        var result = await Handler(profile, store).HandleAsync(command);

        Assert.True(result.Success);
        Assert.Equal(snapshot.StatementId, result.Data?.Id);
        Assert.Equal(snapshot.TransferId, result.Data?.TransferId);
        Assert.Equal(25m, result.Data?.RemainingBalance);
        Assert.Equal(snapshot.CarryStatementId, result.Data?.CarryStatementId);
        Assert.Equal(profile.Id, store.Request?.UserId);
        Assert.Equal(command.FinancialAccountId, store.Request?.FinancialAccountId);
        Assert.Equal(Now, store.Request?.CreatedAt);
        Assert.Contains(CreditCardStatementMessages.SettledSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(CreditCardStatementSettlementOutcome.StatementNotFound,
        CreditCardStatementMessages.NotFound)]
    [InlineData(CreditCardStatementSettlementOutcome.FinancialAccountNotFound,
        CreditCardStatementMessages.FinancialAccountNotFound)]
    [InlineData(CreditCardStatementSettlementOutcome.StatementOpen,
        CreditCardStatementMessages.StatementOpen)]
    [InlineData(CreditCardStatementSettlementOutcome.StatementAlreadySettled,
        CreditCardStatementMessages.StatementAlreadySettled)]
    [InlineData(CreditCardStatementSettlementOutcome.ExchangeRateUnavailable,
        CreditCardStatementMessages.ExchangeRateUnavailable)]
    public async Task GivenSettlementRefusal_WhenHandled_ThenExpectedErrorIsReturned(
        CreditCardStatementSettlementOutcome outcome,
        string error)
    {
        var result = await Handler(Profile(), new StubStore(new(null, outcome)))
            .HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(error, result.Errors);
    }

    [UnitFact]
    public async Task GivenInvalidPayment_WhenHandled_ThenAllFieldsAreReported()
    {
        var store = new StubStore(new(null, CreditCardStatementSettlementOutcome.StatementNotFound));

        var result = await Handler(Profile(), store).HandleAsync(
            new SettleCreditCardStatementCommand
            {
                Amount = 0m,
                PaymentDate = default
            });

        Assert.False(result.Success);
        Assert.Contains(CreditCardStatementMessages.StatementIdRequired, result.Errors);
        Assert.Contains(CreditCardStatementMessages.FinancialAccountIdRequired, result.Errors);
        Assert.Contains(CreditCardStatementMessages.PaymentAmountPositive, result.Errors);
        Assert.Contains(CreditCardStatementMessages.PaymentDateRequired, result.Errors);
        Assert.False(store.Called);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenSettled_ThenStoreIsNotCalled()
    {
        var store = new StubStore(new(Snapshot(), CreditCardStatementSettlementOutcome.Succeeded));

        var result = await Handler(null, store).HandleAsync(Command());

        Assert.Contains(CreditCardStatementMessages.ProfileNotFound, result.Errors);
        Assert.False(store.Called);
    }

    private static SettleCreditCardStatementCommandHandler Handler(
        UserProfileSnapshot? profile,
        ICreditCardStatementSettlementStore store) => new(
        new SettleCreditCardStatementCommandValidator(),
        new ActorAccessor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new Profiles(profile),
        store,
        new FixedTimeProvider(Now));

    private static SettleCreditCardStatementCommand Command() => new()
    {
        Id = Guid.NewGuid(),
        FinancialAccountId = Guid.NewGuid(),
        Amount = 100m,
        PaymentDate = new DateOnly(2026, 9, 4)
    };

    private static CreditCardStatementSettlementSnapshot Snapshot() => new(
        Guid.NewGuid(),
        "Settled",
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        100m,
        "BRL",
        100m,
        "BRL",
        125m,
        25m,
        Guid.NewGuid(),
        0m,
        null,
        null,
        new DateOnly(2026, 9, 4));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private sealed class StubStore(CreditCardStatementSettlementResult result)
        : ICreditCardStatementSettlementStore
    {
        public bool Called { get; private set; }
        public CreditCardStatementSettlement? Request { get; private set; }

        public Task<CreditCardStatementSettlementResult> SettleAsync(
            CreditCardStatementSettlement settlement,
            CancellationToken cancellationToken)
        {
            Called = true;
            Request = settlement;
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
