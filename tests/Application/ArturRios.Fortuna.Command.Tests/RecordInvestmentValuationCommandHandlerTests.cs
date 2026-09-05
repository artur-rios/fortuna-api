using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordInvestmentValuationCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 2, 15, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidValuation_WhenRecorded_ThenValuationAndPositionAreReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var investmentId = Guid.NewGuid();
        var valuationId = Guid.NewGuid();
        var snapshot = Snapshot(valuationId, investmentId, -25m, false);
        var store = new StubValuationStore(Result(snapshot));

        var result = await Handler(subject, Profile(userId, subject), store).HandleAsync(new()
        {
            Id = investmentId,
            Value = -25m,
            ValuedOn = new DateOnly(2026, 9, 4)
        });

        Assert.True(result.Success);
        Assert.Equal(valuationId, result.Data!.Id);
        Assert.Equal(-25m, result.Data.Value);
        Assert.Equal(-20m, result.Data.Position);
        Assert.True(result.Data.IsIndependentlyValued);
        Assert.Equal(userId, store.Record!.UserId);
        Assert.Equal(investmentId, store.Record.InvestmentId);
        Assert.Equal(Now, store.Record.RecordedAt);
        Assert.Contains(InvestmentMessages.ValuationRecordedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenExistingValuation_WhenRecorded_ThenReplacementIsReported()
    {
        var subject = Guid.NewGuid();
        var investmentId = Guid.NewGuid();
        var store = new StubValuationStore(Result(
            Snapshot(Guid.NewGuid(), investmentId, 125m, true)));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand(investmentId));

        Assert.True(result.Success);
        Assert.True(result.Data!.ReplacedExisting);
        Assert.Contains(InvestmentMessages.ValuationReplacedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingInvestment_WhenRecorded_ThenNotFoundIsReturned()
    {
        var subject = Guid.NewGuid();
        var store = new StubValuationStore(new(
            null,
            InvestmentValuationRecordOutcome.InvestmentNotFound));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(InvestmentMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenInvalidFields_WhenRecorded_ThenNothingIsStored()
    {
        var store = new StubValuationStore(Result(null));

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(new()
        {
            Id = Guid.Empty,
            Value = 1234567890123456m,
            ValuedOn = new DateOnly(2026, 9, 6)
        });

        Assert.False(result.Success);
        Assert.Contains(InvestmentMessages.InvestmentIdRequired, result.Errors);
        Assert.Contains(InvestmentMessages.ValuationValuePrecisionInvalid, result.Errors);
        Assert.Contains(InvestmentMessages.ValuedOnFuture, result.Errors);
        Assert.Null(store.Record);
    }

    [UnitFact]
    public async Task GivenMissingDate_WhenRecorded_ThenRequiredErrorIsReturned()
    {
        var store = new StubValuationStore(Result(null));
        var command = ValidCommand();
        command.ValuedOn = default;

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(InvestmentMessages.ValuedOnRequired, result.Errors);
        Assert.Null(store.Record);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenRecorded_ThenNothingIsStored()
    {
        var store = new StubValuationStore(Result(
            Snapshot(Guid.NewGuid(), Guid.NewGuid(), 100m, false)));

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
        var handler = new RecordInvestmentValuationCommandHandler(
            new RecordInvestmentValuationCommandValidator(new FixedTimeProvider(Now)),
            new StubActorAccessor(new RequestActor(userId, 3, null, []) { IsLocal = true }),
            profiles,
            new StubValuationStore(Result(
                Snapshot(Guid.NewGuid(), Guid.NewGuid(), 100m, false))),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static RecordInvestmentValuationCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        IInvestmentValuationStore store) => new(
            new RecordInvestmentValuationCommandValidator(new FixedTimeProvider(Now)),
            new StubActorAccessor(new RequestActor(subject, 3, null, [])),
            new StubUserProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

    private static RecordInvestmentValuationCommand ValidCommand(Guid? investmentId = null) =>
        new()
        {
            Id = investmentId ?? Guid.NewGuid(),
            Value = 100m,
            ValuedOn = new DateOnly(2026, 9, 4)
        };

    private static InvestmentValuationSnapshot Snapshot(
        Guid id,
        Guid investmentId,
        decimal value,
        bool replaced) => new(
        id,
        investmentId,
        value,
        "BRL",
        new DateOnly(2026, 9, 4),
        replaced,
        value + 5m,
        true,
        value,
        new DateOnly(2026, 9, 4),
        Now,
        Now);

    private static InvestmentValuationRecordResult Result(
        InvestmentValuationSnapshot? snapshot) => new(
        snapshot,
        InvestmentValuationRecordOutcome.Succeeded);

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id, subject, "Account Owner", "BRL", false, Now, Now);

    private sealed class StubValuationStore(InvestmentValuationRecordResult result)
        : IInvestmentValuationStore
    {
        public InvestmentValuationRecord? Record { get; private set; }

        public Task<InvestmentValuationRecordResult> RecordAsync(
            InvestmentValuationRecord record,
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
