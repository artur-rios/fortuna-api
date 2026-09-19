using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class ListAuditEntriesQueryHandlerTests
{
    [UnitFact]
    public async Task GivenOwnedEntries_WhenFiltered_ThenOnlyMatchingEntryIsReturned()
    {
        var actorSubject = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        var entries = new StubAuditEntryReader(
            Entry(actorUserId, "DeleteAccountCommand", "Account", targetId,
                AuditOutcome.Refused, "Live transactions still reference this account.", occurredAt),
            Entry(actorUserId, "UpdateAccountCommand", "Account", targetId,
                AuditOutcome.Succeeded, null, occurredAt),
            Entry(Guid.NewGuid(), "DeleteAccountCommand", "Account", targetId,
                AuditOutcome.Refused, "Other user", occurredAt));
        var handler = Handler(Profile(actorUserId, actorSubject), entries);

        var result = await handler.HandleAsync(new ListAuditEntriesQuery
        {
            EntityType = " account ",
            EntityId = targetId,
            Operation = " deleteaccountcommand ",
            Outcome = AuditOutcome.Refused,
            From = occurredAt.AddMinutes(-1),
            To = occurredAt.AddMinutes(1),
            PageNumber = 1,
            PageSize = 10
        });

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalItems);
        var item = Assert.Single(result.Data!);
        Assert.Equal(actorUserId, item.SubjectReference);
        Assert.Equal("DeleteAccountCommand", item.Operation);
        Assert.Equal("Account", item.EntityType);
        Assert.Equal(targetId, item.EntityId);
        Assert.Equal(AuditOutcome.Refused, item.Outcome);
        Assert.Equal("Live transactions still reference this account.", item.Reason);
        Assert.Equal(occurredAt, item.OccurredAt);
        Assert.Contains(AuditEntryMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenSeveralOwnedEntries_WhenPaged_ThenNewestEntriesAreReturnedFirst()
    {
        var actorSubject = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);
        var handler = Handler(
            Profile(actorUserId, actorSubject),
            new StubAuditEntryReader(
                Entry(actorUserId, "First", occurredAt: start),
                Entry(actorUserId, "Second", occurredAt: start.AddMinutes(1)),
                Entry(actorUserId, "Third", occurredAt: start.AddMinutes(2))));

        var result = await handler.HandleAsync(new ListAuditEntriesQuery
        {
            PageNumber = 1,
            PageSize = 2
        });

        Assert.Equal(3, result.TotalItems);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(["Third", "Second"], result.Data!.Select(item => item.Operation));
    }

    [UnitFact]
    public async Task GivenOversizedPage_WhenListed_ThenPageSizeIsCappedLikeOtherLists()
    {
        var actorUserId = Guid.NewGuid();
        var handler = Handler(
            Profile(actorUserId, Guid.NewGuid()),
            new StubAuditEntryReader(
                Entry(actorUserId, "First"),
                Entry(actorUserId, "Second"),
                Entry(actorUserId, "Third")),
            maximumPageSize: 2);

        var result = await handler.HandleAsync(new ListAuditEntriesQuery
        {
            PageNumber = 1,
            PageSize = 500
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Data!.Count);
    }

    [UnitFact]
    public async Task GivenDateOnlyUpperBound_WhenListed_ThenTheWholeDayIsIncluded()
    {
        var actorUserId = Guid.NewGuid();
        var day = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
        var handler = Handler(
            Profile(actorUserId, Guid.NewGuid()),
            new StubAuditEntryReader(
                Entry(actorUserId, "Evening", occurredAt: day.AddHours(23)),
                Entry(actorUserId, "NextDay", occurredAt: day.AddDays(1))));

        var result = await handler.HandleAsync(new ListAuditEntriesQuery
        {
            From = day,
            To = day
        });

        Assert.Equal("Evening", Assert.Single(result.Data!).Operation);
    }

    [UnitFact]
    public async Task GivenInstantUpperBound_WhenListed_ThenItIsInclusiveOfThatInstantOnly()
    {
        var actorUserId = Guid.NewGuid();
        var cutoff = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
        var handler = Handler(
            Profile(actorUserId, Guid.NewGuid()),
            new StubAuditEntryReader(
                Entry(actorUserId, "AtCutoff", occurredAt: cutoff),
                Entry(actorUserId, "After", occurredAt: cutoff.AddMinutes(1))));

        var result = await handler.HandleAsync(new ListAuditEntriesQuery { To = cutoff });

        Assert.Equal("AtCutoff", Assert.Single(result.Data!).Operation);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenListed_ThenProfileIsResolvedByPublicId()
    {
        var actorUserId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile(actorUserId, null));
        var handler = new ListAuditEntriesQueryHandler(
            new CurrentProfileResolver(new StubRequestActorAccessor(new RequestActor(actorUserId, 3, null, [])
            {
                IsLocal = true
            }), profiles),
            new StubAuditEntryReader(Entry(actorUserId, "LocalWrite")),
            new PaginationOptions(100)).Validated(new ListAuditEntriesQueryValidator());

        var result = await handler.HandleAsync(new ListAuditEntriesQuery());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
        Assert.Equal("LocalWrite", Assert.Single(result.Data!).Operation);
    }

    [UnitFact]
    public async Task GivenUnknownActorProfile_WhenListed_ThenNotFoundIsReturned()
    {
        var result = await Handler(null, new StubAuditEntryReader()).HandleAsync(
            new ListAuditEntriesQuery());

        Assert.False(result.Success);
        Assert.Contains(AuditEntryMessages.ProfileNotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenInvalidFilters_WhenListed_ThenEveryValidationErrorIsReturned()
    {
        var result = await Handler(null, new StubAuditEntryReader()).HandleAsync(
            new ListAuditEntriesQuery
            {
                PageNumber = 0,
                PageSize = 0,
                EntityType = new string('e', 101),
                Operation = new string('o', 151),
                Outcome = (AuditOutcome)999,
                From = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero),
                To = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero)
            });

        Assert.False(result.Success);
        Assert.Contains(AuditEntryMessages.InvalidPageNumber, result.Errors);
        Assert.Contains(AuditEntryMessages.InvalidPageSize, result.Errors);
        Assert.Contains(AuditEntryMessages.EntityTypeTooLong, result.Errors);
        Assert.Contains(AuditEntryMessages.OperationTooLong, result.Errors);
        Assert.Contains(AuditEntryMessages.OutcomeInvalid, result.Errors);
        Assert.Contains(AuditEntryMessages.PeriodInvalid, result.Errors);
    }

    private static IPaginatedQueryHandlerAsync<ListAuditEntriesQuery, AuditEntryOutput> Handler(
        UserProfileSnapshot? profile,
        IAuditEntryReader entries,
        int maximumPageSize = 100)
    {
        var subject = profile?.ExternalSubject ?? Guid.NewGuid();

        return new ListAuditEntriesQueryHandler(
            new CurrentProfileResolver(
                new StubRequestActorAccessor(new RequestActor(subject, 3, null, [])),
                new StubUserProfileReader(profile)),
            entries,
            new PaginationOptions(maximumPageSize)).Validated(new ListAuditEntriesQueryValidator());
    }

    private static UserProfileSnapshot Profile(Guid id, Guid? externalSubject) => new(
        id,
        externalSubject,
        "Audit User",
        "BRL",
        false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static AuditEntry Entry(
        Guid actorUserId,
        string operation,
        string? entityType = null,
        Guid? entityId = null,
        AuditOutcome outcome = AuditOutcome.Succeeded,
        string? reason = null,
        DateTimeOffset? occurredAt = null) => new(
            actorUserId,
            operation,
            entityType,
            entityId,
            outcome,
            reason,
            occurredAt ?? DateTimeOffset.UtcNow);

    private sealed class StubAuditEntryReader(params AuditEntry[] entries) : IAuditEntryReader
    {
        public IQueryable<AuditEntry> Query() => entries.AsQueryable();

        public Task<Guid?> FindSubjectReferenceAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult<Guid?>(userId);
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

    private sealed class StubRequestActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }
}
