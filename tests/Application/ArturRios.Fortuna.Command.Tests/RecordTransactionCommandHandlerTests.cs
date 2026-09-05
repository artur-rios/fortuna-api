using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordTransactionCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidTransaction_WhenRecorded_ThenMappedResultIsReturned()
    {
        var profile = Profile();
        var command = ValidCommand();
        var snapshot = Snapshot(command);
        var store = new StubTransactionStore(new TransactionRecordResult(
            snapshot,
            TransactionRecordOutcome.Succeeded));

        var result = await Handler(profile, store).HandleAsync(command);

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(snapshot.CurrencyCode, result.Data?.CurrencyCode);
        Assert.Equal(snapshot.CounterpartyName, result.Data?.CounterpartyName);
        Assert.Single(result.Data!.Tags);
        Assert.Equal(profile.Id, store.Record?.UserId);
        Assert.Equal(Now, store.Record?.CreatedAt);
        Assert.Contains(TransactionMessages.RecordedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(TransactionRecordOutcome.FinancialAccountNotFound,
        TransactionMessages.FinancialAccountNotFound)]
    [InlineData(TransactionRecordOutcome.CreditCardNotFound,
        TransactionMessages.CreditCardNotFound)]
    [InlineData(TransactionRecordOutcome.CategoryNotFound,
        TransactionMessages.CategoryNotFound)]
    [InlineData(TransactionRecordOutcome.CurrencyNotSupported,
        TransactionMessages.CurrencyNotSupported)]
    [InlineData(TransactionRecordOutcome.ExchangeRateUnavailable,
        TransactionMessages.ExchangeRateUnavailable)]
    [InlineData(TransactionRecordOutcome.ConvertedAmountTooSmall,
        TransactionMessages.ConvertedAmountTooSmall)]
    public async Task GivenStoreRefusal_WhenRecorded_ThenCanonicalErrorIsReturned(
        TransactionRecordOutcome outcome,
        string expectedError)
    {
        var store = new StubTransactionStore(new TransactionRecordResult(null, outcome));

        var result = await Handler(Profile(), store).HandleAsync(ValidCommand());

        Assert.Contains(expectedError, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenRecorded_ThenStoreIsNotCalled()
    {
        var store = new StubTransactionStore(new TransactionRecordResult(
            Snapshot(ValidCommand()),
            TransactionRecordOutcome.Succeeded));

        var result = await Handler(null, store).HandleAsync(ValidCommand());

        Assert.Contains(TransactionMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Record);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenRecorded_ThenProfileIsResolvedByPublicId()
    {
        var profile = Profile();
        var profiles = new StubProfileReader(profile);
        var store = new StubTransactionStore(new TransactionRecordResult(
            Snapshot(ValidCommand()),
            TransactionRecordOutcome.Succeeded));
        var handler = new RecordTransactionCommandHandler(
            new RecordTransactionCommandValidator(new FixedTimeProvider(Now)),
            new StubActor(new RequestActor(profile.Id, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static RecordTransactionCommandHandler Handler(
        UserProfileSnapshot? profile,
        ITransactionStore store) => new(
        new RecordTransactionCommandValidator(new FixedTimeProvider(Now)),
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfileReader(profile),
        store,
        new FixedTimeProvider(Now));

    private static RecordTransactionCommand ValidCommand() => new()
    {
        OccurredOn = new DateOnly(2026, 9, 5),
        Amount = 25m,
        Direction = TransactionDirection.Expense,
        FinancialAccountId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        CurrencyCode = "USD",
        Description = "Lunch",
        Counterparty = "Cafe",
        Tags = ["Food"]
    };

    private static TransactionSnapshot Snapshot(RecordTransactionCommand command) => new(
        Guid.NewGuid(),
        command.FinancialAccountId,
        null,
        command.CategoryId,
        "Dining",
        command.Direction,
        125m,
        "BRL",
        command.Amount,
        command.CurrencyCode,
        5m,
        command.OccurredOn,
        command.OccurredOn,
        command.Description,
        Guid.NewGuid(),
        command.Counterparty,
        [new TransactionTagSnapshot(Guid.NewGuid(), "Food")],
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        Now,
        Now);

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubTransactionStore(TransactionRecordResult result) : ITransactionStore
    {
        public TransactionRecord? Record { get; private set; }

        public Task<TransactionRecordResult> RecordAsync(
            TransactionRecord record,
            CancellationToken cancellationToken)
        {
            Record = record;
            return Task.FromResult(result);
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
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

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
