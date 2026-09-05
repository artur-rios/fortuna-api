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

public sealed class UpdateRecurringTransactionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedRule_WhenUpdated_ThenForwardOnlyResultReturns()
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var updater = new StubUpdater(new(
            snapshot, RecurringTransactionUpdateOutcome.Succeeded));

        var result = await Handler(profile, updater).HandleAsync(Command(snapshot.Id));

        Assert.True(result.Success);
        Assert.Equal(snapshot.NextOccurrences.First(), result.Data?.AppliesFrom);
        Assert.False(result.Data?.MaterializedOccurrencesChanged);
        Assert.Equal(profile.Id, updater.Update?.UserId);
    }

    [UnitTheory]
    [InlineData(RecurringTransactionUpdateOutcome.NotFound, RecurringTransactionMessages.NotFound)]
    [InlineData(RecurringTransactionUpdateOutcome.FinancialAccountNotFound,
        RecurringTransactionMessages.FinancialAccountNotFound)]
    [InlineData(RecurringTransactionUpdateOutcome.CreditCardNotFound,
        RecurringTransactionMessages.CreditCardNotFound)]
    [InlineData(RecurringTransactionUpdateOutcome.CategoryNotFound,
        RecurringTransactionMessages.CategoryNotFound)]
    public async Task GivenStoreRefusal_WhenUpdated_ThenCanonicalErrorReturns(
        RecurringTransactionUpdateOutcome outcome,
        string expected)
    {
        var result = await Handler(Profile(), new StubUpdater(new(null, outcome)))
            .HandleAsync(Command(Guid.NewGuid()));

        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenUpdated_ThenStoreIsNotCalled()
    {
        var updater = new StubUpdater(new(Snapshot(), RecurringTransactionUpdateOutcome.Succeeded));

        var result = await Handler(null, updater).HandleAsync(Command(Guid.NewGuid()));

        Assert.Contains(RecurringTransactionMessages.ProfileNotFound, result.Errors);
        Assert.Null(updater.Update);
    }

    private static UpdateRecurringTransactionCommandHandler Handler(
        UserProfileSnapshot? profile,
        IRecurringTransactionUpdater updater) => new(
        new UpdateRecurringTransactionCommandValidator(),
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfiles(profile),
        updater,
        new FixedTimeProvider(Now));

    private static UpdateRecurringTransactionCommand Command(Guid id) => new()
    {
        Id = id,
        FinancialAccountId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        Direction = TransactionDirection.Expense,
        Amount = 25m,
        Frequency = RecurrenceFrequency.Monthly,
        StartsOn = new DateOnly(2026, 9, 10)
    };

    private static RecurringTransactionSnapshot Snapshot() => new()
    {
        Id = Guid.NewGuid(),
        FinancialAccountId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        Direction = TransactionDirection.Expense,
        Amount = 25m,
        CurrencyCode = "BRL",
        Frequency = RecurrenceFrequency.Monthly,
        StartsOn = new DateOnly(2026, 9, 10),
        LastMaterializedOn = new DateOnly(2026, 9, 5),
        NextOccurrences = [new DateOnly(2026, 9, 10)],
        CreatedAt = Now.AddMonths(-1),
        UpdatedAt = Now
    };

    private sealed class StubUpdater(RecurringTransactionUpdateResult result)
        : IRecurringTransactionUpdater
    {
        public RecurringTransactionUpdate? Update { get; private set; }

        public Task<RecurringTransactionUpdateResult> UpdateAsync(
            RecurringTransactionUpdate update,
            CancellationToken cancellationToken)
        {
            Update = update;
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
