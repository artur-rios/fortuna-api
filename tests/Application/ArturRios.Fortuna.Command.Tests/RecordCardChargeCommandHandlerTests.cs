using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordCardChargeCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 20, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedCard_WhenChargeRecorded_ThenStatementAssignmentIsReturned()
    {
        var profile = Profile();
        var cardId = Guid.NewGuid();
        var statementId = Guid.NewGuid();
        var store = new StubStore(new CardChargeCreationResult(new CardChargeSnapshot(
            Guid.NewGuid(), cardId, 25m, new DateOnly(2026, 9, 4), false,
            statementId, new DateOnly(2026, 8, 21), new DateOnly(2026, 9, 20),
            new DateOnly(2026, 9, 20), new DateOnly(2026, 10, 5), "Open", 25m), false));
        var handler = Handler(profile, store);

        var result = await handler.HandleAsync(new RecordCardChargeCommand
        {
            CreditCardId = cardId,
            Amount = 25m,
            OccurredOn = new DateOnly(2026, 9, 4)
        });

        Assert.True(result.Success);
        Assert.Equal(statementId, result.Data?.StatementId);
        Assert.Equal(25m, result.Data?.StatementPurchaseTotal);
        Assert.Equal(profile.Id, store.Creation?.UserId);
        Assert.Equal(Now, store.Creation?.CreatedAt);
        Assert.Contains(TransactionMessages.CardChargeCreatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenForeignOrMissingCard_WhenChargeRecorded_ThenNotFoundIsReturned()
    {
        var store = new StubStore(new CardChargeCreationResult(null, true));

        var result = await Handler(Profile(), store).HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(TransactionMessages.CreditCardNotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenChargeRecorded_ThenStoreIsNotCalled()
    {
        var store = new StubStore(new CardChargeCreationResult(null, true));

        var result = await Handler(null, store).HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(TransactionMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenInvalidCharge_WhenValidated_ThenEveryFieldIsNamed()
    {
        var result = await new RecordCardChargeCommandValidator().ValidateAsync(
            new RecordCardChargeCommand());

        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == TransactionMessages.CreditCardIdRequired);
        Assert.Contains(result.Errors, item => item.ErrorMessage == TransactionMessages.AmountPositive);
        Assert.Contains(result.Errors, item =>
            item.ErrorMessage == TransactionMessages.OccurredOnRequired);
    }

    private static RecordCardChargeCommandHandler Handler(
        UserProfileSnapshot? profile,
        ICardChargeStore store) => new(
        new RecordCardChargeCommandValidator(),
        new StubActorAccessor(new RequestActor(
            profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfiles(profile),
        store,
        new FixedTimeProvider(Now));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private static RecordCardChargeCommand ValidCommand() => new()
    {
        CreditCardId = Guid.NewGuid(),
        Amount = 10m,
        OccurredOn = new DateOnly(2026, 9, 4)
    };

    private sealed class StubStore(CardChargeCreationResult result) : ICardChargeStore
    {
        public CardChargeCreation? Creation { get; private set; }

        public Task<CardChargeCreationResult> CreateAsync(
            CardChargeCreation creation,
            CancellationToken cancellationToken)
        {
            Creation = creation;
            return Task.FromResult(result);
        }
    }

    private sealed class StubProfiles(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActorAccessor(RequestActor actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
