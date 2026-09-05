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

public sealed class DefineRecurringTransactionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedTemplate_WhenDefined_ThenRuleIsReturned()
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var store = new StubStore(new(snapshot, RecurringTransactionRecordOutcome.Succeeded));

        var result = await Handler(profile, store).HandleAsync(Command());

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(snapshot.NextOccurrences, result.Data?.NextOccurrences);
        Assert.Equal(profile.Id, store.Record?.UserId);
        Assert.Equal(new DateOnly(2026, 9, 5), store.Record?.PreviewFrom);
    }

    [UnitTheory]
    [InlineData(RecurringTransactionRecordOutcome.FinancialAccountNotFound,
        RecurringTransactionMessages.FinancialAccountNotFound)]
    [InlineData(RecurringTransactionRecordOutcome.CreditCardNotFound,
        RecurringTransactionMessages.CreditCardNotFound)]
    [InlineData(RecurringTransactionRecordOutcome.CategoryNotFound,
        RecurringTransactionMessages.CategoryNotFound)]
    public async Task GivenStoreRefusal_WhenDefined_ThenCanonicalErrorReturns(
        RecurringTransactionRecordOutcome outcome,
        string expected)
    {
        var result = await Handler(Profile(), new StubStore(new(null, outcome)))
            .HandleAsync(Command());

        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenDefined_ThenStoreIsNotCalled()
    {
        var store = new StubStore(new(Snapshot(), RecurringTransactionRecordOutcome.Succeeded));

        var result = await Handler(null, store).HandleAsync(Command());

        Assert.Contains(RecurringTransactionMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Record);
    }

    private static DefineRecurringTransactionCommandHandler Handler(
        UserProfileSnapshot? profile,
        IRecurringTransactionStore store) => new(
        new DefineRecurringTransactionCommandValidator(),
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfiles(profile),
        store,
        new FixedTimeProvider(Now));

    private static DefineRecurringTransactionCommand Command() => new()
    {
        FinancialAccountId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        Direction = TransactionDirection.Expense,
        Amount = 10m,
        Frequency = RecurrenceFrequency.Monthly,
        StartsOn = new DateOnly(2026, 9, 30)
    };

    private static RecurringTransactionSnapshot Snapshot() => new()
    {
        Id = Guid.NewGuid(),
        FinancialAccountId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        Direction = TransactionDirection.Expense,
        Amount = 10m,
        CurrencyCode = "BRL",
        Frequency = RecurrenceFrequency.Monthly,
        StartsOn = new DateOnly(2026, 9, 30),
        NextOccurrences = [new DateOnly(2026, 9, 30)],
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private sealed class StubStore(RecurringTransactionRecordResult result) : IRecurringTransactionStore
    {
        public RecurringTransactionRecord? Record { get; private set; }
        public Task<RecurringTransactionRecordResult> RecordAsync(
            RecurringTransactionRecord record,
            CancellationToken cancellationToken)
        {
            Record = record;
            return Task.FromResult(result);
        }
    }

    private sealed class StubProfiles(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(Guid id, CancellationToken token) => Task.FromResult(profile);
        public Task<UserProfileSnapshot?> FindByPublicIdAsync(Guid id, CancellationToken token) => Task.FromResult(profile);
    }

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor { public RequestActor? Actor => actor; }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private static UserProfileSnapshot Profile() => new(Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);
}
