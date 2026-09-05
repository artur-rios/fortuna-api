using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordInvestmentMovementCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 23, 45, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidMovement_WhenRecorded_ThenPositionAndFundingAreReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var investmentId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var snapshot = new InvestmentMovementSnapshot(
            Guid.NewGuid(), investmentId, InvestmentMovementType.Contribution,
            500m, "BRL", new DateOnly(2026, 9, 4), 725m, accountId, 100m,
            "USD", Guid.NewGuid(), Guid.NewGuid(), 5m,
            new DateOnly(2026, 9, 3), Now, Now);
        var store = new StubMovementStore(Result(snapshot));

        var result = await Handler(subject, Profile(userId, subject), store).HandleAsync(new()
        {
            Id = investmentId,
            MovementType = InvestmentMovementType.Contribution,
            Amount = 100m,
            OccurredOn = new DateOnly(2026, 9, 4),
            FinancialAccountId = accountId
        });

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data!.Id);
        Assert.Equal(500m, result.Data.Amount);
        Assert.Equal(725m, result.Data.Position);
        Assert.Equal(5m, result.Data.AppliedRate);
        Assert.Equal(userId, store.Record!.UserId);
        Assert.Equal(100m, store.Record.Amount);
        Assert.Equal(accountId, store.Record.FinancialAccountId);
        Assert.Equal(Now, store.Record.CreatedAt);
        Assert.Contains(InvestmentMessages.MovementRecordedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(InvestmentMovementRecordOutcome.InvestmentNotFound,
        InvestmentMessages.NotFound)]
    [InlineData(InvestmentMovementRecordOutcome.FinancialAccountNotFound,
        InvestmentMessages.FinancialAccountNotFound)]
    [InlineData(InvestmentMovementRecordOutcome.ExchangeRateUnavailable,
        InvestmentMessages.ExchangeRateUnavailable)]
    [InlineData(InvestmentMovementRecordOutcome.ConvertedAmountTooSmall,
        InvestmentMessages.ConvertedAmountTooSmall)]
    public async Task GivenStoreRefusal_WhenRecorded_ThenExpectedErrorIsReturned(
        InvestmentMovementRecordOutcome outcome,
        string expectedError)
    {
        var subject = Guid.NewGuid();
        var store = new StubMovementStore(new(null, outcome));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Errors);
    }

    [UnitFact]
    public async Task GivenInvalidMovement_WhenRecorded_ThenAllValidationErrorsAreReturned()
    {
        var store = new StubMovementStore(Result(null));

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(new()
        {
            Id = Guid.Empty,
            MovementType = (InvestmentMovementType)99,
            Amount = 0m,
            OccurredOn = new DateOnly(2026, 9, 6),
            FinancialAccountId = Guid.Empty
        });

        Assert.False(result.Success);
        Assert.Contains(InvestmentMessages.InvestmentIdRequired, result.Errors);
        Assert.Contains(InvestmentMessages.MovementTypeInvalid, result.Errors);
        Assert.Contains(InvestmentMessages.MovementAmountPositive, result.Errors);
        Assert.Contains(InvestmentMessages.OccurredOnTooFarInFuture, result.Errors);
        Assert.Contains(InvestmentMessages.FinancialAccountIdInvalid, result.Errors);
        Assert.Contains(InvestmentMessages.FundingRequiresContribution, result.Errors);
        Assert.Null(store.Record);
    }

    [UnitFact]
    public async Task GivenTomorrow_WhenRecorded_ThenDateIsAccepted()
    {
        var subject = Guid.NewGuid();
        var snapshot = Snapshot(new DateOnly(2026, 9, 5));
        var store = new StubMovementStore(Result(snapshot));
        var command = ValidCommand();
        command.OccurredOn = new DateOnly(2026, 9, 5);

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(command);

        Assert.True(result.Success);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenRecorded_ThenNothingIsStored()
    {
        var store = new StubMovementStore(Result(Snapshot(new DateOnly(2026, 9, 4))));

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(InvestmentMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Record);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenRecorded_ThenProfileIsResolvedByPublicId()
    {
        var userId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile(userId, null));
        var handler = new RecordInvestmentMovementCommandHandler(
            new RecordInvestmentMovementCommandValidator(new FixedTimeProvider(Now)),
            new StubActorAccessor(new RequestActor(userId, 3, null, []) { IsLocal = true }),
            profiles,
            new StubMovementStore(Result(Snapshot(new DateOnly(2026, 9, 4)))),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static RecordInvestmentMovementCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        IInvestmentMovementStore store) => new(
            new RecordInvestmentMovementCommandValidator(new FixedTimeProvider(Now)),
            new StubActorAccessor(new RequestActor(subject, 3, null, [])),
            new StubUserProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

    private static RecordInvestmentMovementCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        MovementType = InvestmentMovementType.Contribution,
        Amount = 100m,
        OccurredOn = new DateOnly(2026, 9, 4)
    };

    private static InvestmentMovementSnapshot Snapshot(DateOnly occurredOn) => new(
        Guid.NewGuid(), Guid.NewGuid(), InvestmentMovementType.Contribution,
        100m, "BRL", occurredOn, 100m, null, null, null, null, null,
        null, null, Now, Now);

    private static InvestmentMovementRecordResult Result(InvestmentMovementSnapshot? snapshot) =>
        new(snapshot, InvestmentMovementRecordOutcome.Succeeded);

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id, subject, "Account Owner", "BRL", false, Now, Now);

    private sealed class StubMovementStore(InvestmentMovementRecordResult result)
        : IInvestmentMovementStore
    {
        public InvestmentMovementRecord? Record { get; private set; }

        public Task<InvestmentMovementRecordResult> RecordAsync(
            InvestmentMovementRecord record,
            CancellationToken cancellationToken)
        {
            Record = record;
            return Task.FromResult(result);
        }
    }

    private sealed class StubUserProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public bool PublicIdLookupUsed { get; private set; }

        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken)
        {
            PublicIdLookupUsed = true;
            return Task.FromResult(profile);
        }
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
