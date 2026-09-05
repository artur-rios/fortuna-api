using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class RecurringTransactionQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedRule_WhenRead_ThenSnapshotIsMapped()
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var reader = new StubReader(snapshot);

        var result = await Handler(profile, reader).HandleAsync(
            new GetRecurringTransactionByIdQuery { Id = snapshot.Id });

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(snapshot.NextOccurrences, result.Data?.NextOccurrences);
        Assert.Equal(profile.Id, reader.UserId);
    }

    [UnitTheory]
    [InlineData(true, RecurringTransactionMessages.ProfileNotFound)]
    [InlineData(false, RecurringTransactionMessages.NotFound)]
    public async Task GivenMissingDependency_WhenRead_ThenCanonicalErrorReturns(
        bool missingProfile,
        string expected)
    {
        var result = await Handler(missingProfile ? null : Profile(), new StubReader(null))
            .HandleAsync(new GetRecurringTransactionByIdQuery { Id = Guid.NewGuid() });

        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenEmptyId_WhenRead_ThenValidationErrorReturns()
    {
        var result = await Handler(Profile(), new StubReader(Snapshot()))
            .HandleAsync(new GetRecurringTransactionByIdQuery());

        Assert.Contains(RecurringTransactionMessages.IdRequired, result.Errors);
    }

    private static GetRecurringTransactionByIdQueryHandler Handler(
        UserProfileSnapshot? profile,
        IRecurringTransactionReader reader) => new(
        new GetRecurringTransactionByIdQueryValidator(),
        new StubProfiles(profile),
        reader,
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])));

    private static RecurringTransactionSnapshot Snapshot() => new()
    {
        Id = Guid.NewGuid(),
        FinancialAccountId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        Direction = TransactionDirection.Expense,
        Amount = 10m,
        CurrencyCode = "BRL",
        Frequency = RecurrenceFrequency.Weekly,
        StartsOn = new DateOnly(2026, 9, 5),
        NextOccurrences = [new DateOnly(2026, 9, 5)],
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private sealed class StubReader(RecurringTransactionSnapshot? snapshot) : IRecurringTransactionReader
    {
        public Guid? UserId { get; private set; }
        public Task<RecurringTransactionSnapshot?> FindByIdAsync(Guid userId, Guid id, CancellationToken token)
        {
            UserId = userId;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class StubProfiles(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(Guid id, CancellationToken token) => Task.FromResult(profile);
        public Task<UserProfileSnapshot?> FindByPublicIdAsync(Guid id, CancellationToken token) => Task.FromResult(profile);
    }

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor { public RequestActor? Actor => actor; }
    private static UserProfileSnapshot Profile() => new(Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);
}
