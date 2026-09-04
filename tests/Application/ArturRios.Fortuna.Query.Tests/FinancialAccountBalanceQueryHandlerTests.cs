using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class FinancialAccountBalanceQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 15, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedAccount_WhenBalanceRequested_ThenDerivedBalanceIsReturned()
    {
        var profile = Profile();
        var accountId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 8, 31);
        var reader = new StubFinancialAccountReader(
            profile.Id,
            new FinancialAccountBalanceSnapshot(accountId, "BRL", 123.4567m, asOf));
        var handler = Handler(profile, reader);

        var result = await handler.HandleAsync(new GetFinancialAccountBalanceQuery
        {
            Id = accountId,
            AsOf = asOf
        });

        Assert.True(result.Success);
        Assert.Equal(accountId, result.Data?.Id);
        Assert.Equal("BRL", result.Data?.CurrencyCode);
        Assert.Equal(123.4567m, result.Data?.Balance);
        Assert.Equal(asOf, result.Data?.AsOf);
        Assert.Equal(asOf, reader.RequestedAsOf);
        Assert.Contains(FinancialAccountMessages.BalanceRetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenNoAsOfDate_WhenBalanceRequested_ThenCurrentUtcDateIsUsed()
    {
        var profile = Profile();
        var accountId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var reader = new StubFinancialAccountReader(
            profile.Id,
            new FinancialAccountBalanceSnapshot(accountId, "USD", 10m, today));

        var result = await Handler(profile, reader).HandleAsync(
            new GetFinancialAccountBalanceQuery { Id = accountId });

        Assert.True(result.Success);
        Assert.Equal(today, result.Data?.AsOf);
        Assert.Equal(today, reader.RequestedAsOf);
    }

    [UnitFact]
    public async Task GivenMissingOrForeignAccount_WhenBalanceRequested_ThenSameNotFoundIsReturned()
    {
        var profile = Profile();
        var accountId = Guid.NewGuid();
        var otherProfile = Profile();
        var reader = new StubFinancialAccountReader(
            otherProfile.Id,
            new FinancialAccountBalanceSnapshot(
                accountId,
                "BRL",
                10m,
                DateOnly.FromDateTime(Now.UtcDateTime)));
        var handler = Handler(profile, reader);

        var foreign = await handler.HandleAsync(new GetFinancialAccountBalanceQuery { Id = accountId });
        var missing = await handler.HandleAsync(new GetFinancialAccountBalanceQuery { Id = Guid.NewGuid() });

        Assert.False(foreign.Success);
        Assert.False(missing.Success);
        Assert.Equal(foreign.Errors, missing.Errors);
        Assert.Contains(FinancialAccountMessages.NotFound, foreign.Errors);
    }

    [UnitFact]
    public async Task GivenUnknownActorProfile_WhenBalanceRequested_ThenProfileNotFoundIsReturned()
    {
        var reader = new StubFinancialAccountReader(Guid.NewGuid(), null);

        var result = await Handler(null, reader).HandleAsync(new GetFinancialAccountBalanceQuery());

        Assert.False(result.Success);
        Assert.Contains(FinancialAccountMessages.ProfileNotFound, result.Errors);
        Assert.False(reader.WasCalled);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenBalanceRequested_ThenProfileIsResolvedByPublicId()
    {
        var profile = Profile(externalSubject: null);
        var accountId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(profile);
        var reader = new StubFinancialAccountReader(
            profile.Id,
            new FinancialAccountBalanceSnapshot(
                accountId,
                "BRL",
                10m,
                DateOnly.FromDateTime(Now.UtcDateTime)));
        var handler = new GetFinancialAccountBalanceQueryHandler(
            profiles,
            reader,
            new StubRequestActorAccessor(new RequestActor(profile.Id, 3, null, [])
            {
                IsLocal = true
            }),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GetFinancialAccountBalanceQuery { Id = accountId });

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static GetFinancialAccountBalanceQueryHandler Handler(
        UserProfileSnapshot? profile,
        IFinancialAccountReader accounts) => new(
        new StubUserProfileReader(profile),
        accounts,
        new StubRequestActorAccessor(new RequestActor(
            profile?.ExternalSubject ?? Guid.NewGuid(),
            3,
            null,
            [])),
        new FixedTimeProvider(Now));

    private static UserProfileSnapshot Profile(Guid? externalSubject = default) => new(
        Guid.NewGuid(),
        externalSubject ?? Guid.NewGuid(),
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubFinancialAccountReader(
        Guid ownerId,
        FinancialAccountBalanceSnapshot? balance) : IFinancialAccountReader
    {
        public bool WasCalled { get; private set; }
        public DateOnly? RequestedAsOf { get; private set; }

        public IQueryable<FinancialAccount> Query() => Array.Empty<FinancialAccount>().AsQueryable();

        public Task<FinancialAccountSnapshot?> FindByIdAsync(
            Guid userId,
            Guid id,
            bool includeDeleted,
            CancellationToken cancellationToken) => Task.FromResult<FinancialAccountSnapshot?>(null);

        public Task<FinancialAccountBalanceSnapshot?> CalculateBalanceAsync(
            Guid userId,
            Guid id,
            DateOnly asOf,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            RequestedAsOf = asOf;
            return Task.FromResult(
                userId == ownerId && balance?.Id == id
                    ? balance
                    : null);
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

    private sealed class StubRequestActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
