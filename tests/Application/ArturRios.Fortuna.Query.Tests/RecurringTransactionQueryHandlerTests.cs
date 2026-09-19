using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
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

    [UnitFact]
    public async Task GivenOwnedRules_WhenListed_ThenTheyAreScopedToTheActorAndPaginated()
    {
        // Given
        var profile = Profile();
        var snapshot = Snapshot();
        var reader = new StubReader(snapshot);

        // When
        var result = await ListHandler(profile, reader).HandleAsync(
            new ListRecurringTransactionsQuery { PageNumber = 1, PageSize = 20 });

        // Then
        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, Assert.Single(result.Data!).Id);
        Assert.Equal(snapshot.NextOccurrences, result.Data!.Single().NextOccurrences);
        Assert.Equal(profile.Id, reader.Criteria?.UserId);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(1, result.TotalItems);
        Assert.Contains(RecurringTransactionMessages.ListedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenNoRules_WhenListed_ThenAnEmptyPageSucceeds()
    {
        // Given
        var reader = new StubReader(null);

        // When
        var result = await ListHandler(Profile(), reader).HandleAsync(
            new ListRecurringTransactionsQuery());

        // Then
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
        Assert.Equal(0, result.TotalItems);
    }

    [UnitTheory]
    [InlineData(null, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task GivenListCriteria_WhenListed_ThenTheyReachTheReaderUnchanged(
        bool? active,
        bool includeDeleted)
    {
        // Given
        var reader = new StubReader(Snapshot());

        // When
        await ListHandler(Profile(), reader).HandleAsync(new ListRecurringTransactionsQuery
        {
            Active = active,
            IncludeDeleted = includeDeleted,
            SortBy = "Amount",
            Descending = true
        });

        // Then
        Assert.Equal(active, reader.Criteria?.Active);
        Assert.Equal(includeDeleted, reader.Criteria?.IncludeDeleted);
        Assert.Equal("Amount", reader.Criteria?.SortBy);
        Assert.True(reader.Criteria?.Descending);
    }

    [UnitFact]
    public async Task GivenPageSizeAboveTheMaximum_WhenListed_ThenItIsCappedBeforeTheReaderIsAsked()
    {
        // Given
        var reader = new StubReader(Snapshot());

        // When
        var result = await ListHandler(Profile(), reader, maximumPageSize: 2).HandleAsync(
            new ListRecurringTransactionsQuery { PageSize = 500 });

        // Then
        Assert.Equal(2, reader.Criteria?.PageSize);
        Assert.Equal(2, result.PageSize);
    }

    [UnitFact]
    public async Task GivenUnknownActorProfile_WhenListed_ThenNotFoundIsReturned()
    {
        // Given
        var reader = new StubReader(Snapshot());

        // When
        var result = await ListHandler(null, reader).HandleAsync(
            new ListRecurringTransactionsQuery());

        // Then
        Assert.Contains(RecurringTransactionMessages.ProfileNotFound, result.Errors);
        Assert.Null(reader.Criteria);
    }

    [UnitFact]
    public async Task GivenInvalidListCriteria_WhenListed_ThenFieldsAreNamedInErrors()
    {
        // Given
        var reader = new StubReader(Snapshot());

        // When
        var result = await ListHandler(Profile(), reader).HandleAsync(
            new ListRecurringTransactionsQuery
            {
                PageNumber = 0,
                PageSize = 0,
                SortBy = "Counterparty"
            });

        // Then
        Assert.False(result.Success);
        Assert.Contains(RecurringTransactionMessages.InvalidPageNumber, result.Errors);
        Assert.Contains(RecurringTransactionMessages.InvalidPageSize, result.Errors);
        Assert.Contains(RecurringTransactionMessages.SortByUnsupported, result.Errors);
        Assert.Null(reader.Criteria);
    }

    private static IQueryHandlerAsync<GetRecurringTransactionByIdQuery, RecurringTransactionOutput> Handler(
        UserProfileSnapshot? profile,
        IRecurringTransactionReader reader) => new GetRecurringTransactionByIdQueryHandler(
        new CurrentProfileResolver(
            new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
            new StubProfiles(profile)),
        reader).Validated(new GetRecurringTransactionByIdQueryValidator());

    private static IPaginatedQueryHandlerAsync<ListRecurringTransactionsQuery, RecurringTransactionOutput> ListHandler(
        UserProfileSnapshot? profile,
        IRecurringTransactionReader reader,
        int maximumPageSize = 100) => new ListRecurringTransactionsQueryHandler(
        new CurrentProfileResolver(
            new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
            new StubProfiles(profile)),
        reader,
        new PaginationOptions(maximumPageSize)).Validated(new ListRecurringTransactionsQueryValidator());

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
        private readonly RecurringTransactionSnapshot[] page =
            snapshot is null ? [] : [snapshot];

        public Guid? UserId { get; private set; }
        public RecurringTransactionListCriteria? Criteria { get; private set; }

        public Task<RecurringTransactionSnapshot?> FindByIdAsync(Guid userId, Guid id, CancellationToken token)
        {
            UserId = userId;

            return Task.FromResult(snapshot);
        }

        public Task<RecurringTransactionListPage> ListAsync(
            RecurringTransactionListCriteria criteria,
            CancellationToken token)
        {
            Criteria = criteria;

            return Task.FromResult(new RecurringTransactionListPage(page, page.Length));
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
