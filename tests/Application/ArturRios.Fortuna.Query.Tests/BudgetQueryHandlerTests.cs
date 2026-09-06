using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class BudgetQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedBudgets_WhenListed_ThenSnapshotsAreReturned()
    {
        var profile = Profile();
        var store = new StubBudgetReader([Snapshot()]);
        var handler = new ListBudgetsQueryHandler(
            Actor(profile),
            new StubProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ListBudgetsQuery
        {
            IncludeDeleted = true
        });

        Assert.True(result.Success);
        var budget = Assert.Single(result.Data!.Budgets);
        Assert.Equal(125m, budget.CurrentPeriod.Spent);
        Assert.Equal(profile.Id, store.UserId);
        Assert.True(store.IncludeDeleted);
        Assert.Equal(new DateOnly(2026, 9, 6), store.AsOf);
        Assert.Contains(BudgetMessages.ListedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenOwnedBudget_WhenRetrieved_ThenSnapshotIsReturned()
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var store = new StubBudgetReader([], snapshot);
        var handler = new GetBudgetByIdQueryHandler(
            Actor(profile),
            new StubProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GetBudgetByIdQuery
        {
            Id = snapshot.Id
        });

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(snapshot.Categories.Single().Name, result.Data?.Categories.Single().Name);
        Assert.Equal(snapshot.Id, store.BudgetId);
        Assert.Contains(BudgetMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingBudget_WhenRetrieved_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var handler = new GetBudgetByIdQueryHandler(
            Actor(profile),
            new StubProfileReader(profile),
            new StubBudgetReader([]),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GetBudgetByIdQuery
        {
            Id = Guid.NewGuid()
        });

        Assert.False(result.Success);
        Assert.Contains(BudgetMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenListed_ThenStoreIsNotCalled()
    {
        var store = new StubBudgetReader([]);
        var handler = new ListBudgetsQueryHandler(
            Actor(null),
            new StubProfileReader(null),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new ListBudgetsQuery());

        Assert.False(result.Success);
        Assert.Null(store.UserId);
        Assert.Contains(BudgetMessages.ProfileNotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenBudgetPeriod_WhenConsumptionRequested_ThenConversionIsReturned()
    {
        var profile = Profile();
        var consumption = Consumption();
        var store = new StubBudgetReader([])
        {
            ConsumptionResult = new BudgetConsumptionResult(
                consumption,
                BudgetConsumptionOutcome.Succeeded)
        };
        var handler = new GetBudgetConsumptionQueryHandler(
            Actor(profile),
            new StubProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GetBudgetConsumptionQuery
        {
            Id = consumption.BudgetId,
            PeriodStart = new DateOnly(2026, 8, 1)
        });

        Assert.True(result.Success);
        Assert.Equal(250m, result.Data?.Spent);
        var conversion = Assert.Single(result.Data!.Conversions);
        Assert.Equal(5m, conversion.AppliedRate);
        Assert.Equal(ExchangeRateSource.Manual, conversion.RateSource);
        Assert.Equal(new DateOnly(2026, 8, 1), store.AsOf);
        Assert.Contains(BudgetMessages.ConsumptionRetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenDateBeforeBudget_WhenConsumptionRequested_ThenEmptyReasonIsReturned()
    {
        var profile = Profile();
        var consumption = Consumption(isCovered: false);
        var store = new StubBudgetReader([])
        {
            ConsumptionResult = new BudgetConsumptionResult(
                consumption,
                BudgetConsumptionOutcome.PeriodPrecedesBudget)
        };
        var handler = new GetBudgetConsumptionQueryHandler(
            Actor(profile),
            new StubProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GetBudgetConsumptionQuery
        {
            Id = consumption.BudgetId
        });

        Assert.True(result.Success);
        Assert.False(result.Data?.IsCovered);
        Assert.Equal(BudgetMessages.PeriodPrecedesBudget, result.Data?.Reason);
        Assert.Equal(new DateOnly(2026, 9, 6), store.AsOf);
        Assert.Contains(BudgetMessages.PeriodPrecedesBudget, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingBudget_WhenConsumptionRequested_ThenNotFoundIsReturned()
    {
        var profile = Profile();
        var store = new StubBudgetReader([])
        {
            ConsumptionResult = new BudgetConsumptionResult(
                null,
                BudgetConsumptionOutcome.NotFound)
        };
        var handler = new GetBudgetConsumptionQueryHandler(
            Actor(profile),
            new StubProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GetBudgetConsumptionQuery
        {
            Id = Guid.NewGuid()
        });

        Assert.False(result.Success);
        Assert.Contains(BudgetMessages.NotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenConsumptionRequested_ThenStoreIsNotCalled()
    {
        var store = new StubBudgetReader([]);
        var handler = new GetBudgetConsumptionQueryHandler(
            Actor(null),
            new StubProfileReader(null),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GetBudgetConsumptionQuery
        {
            Id = Guid.NewGuid()
        });

        Assert.False(result.Success);
        Assert.Null(store.UserId);
        Assert.Contains(BudgetMessages.ProfileNotFound, result.Errors);
    }

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private static BudgetSnapshot Snapshot() => new(
        Guid.NewGuid(),
        500m,
        "BRL",
        BudgetPeriodType.Monthly,
        new DateOnly(2026, 9, 1),
        true,
        [new BudgetCategorySnapshot(Guid.NewGuid(), "Dining")],
        new BudgetConsumptionSnapshot(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            125m,
            375m,
            false,
            0m,
            true),
        false,
        Now,
        Now);

    private static BudgetConsumptionDetailSnapshot Consumption(bool isCovered = true) => new(
        Guid.NewGuid(),
        500m,
        "BRL",
        new DateOnly(2026, 8, 1),
        isCovered ? new DateOnly(2026, 8, 1) : null,
        isCovered ? new DateOnly(2026, 8, 31) : null,
        isCovered ? 250m : null,
        isCovered ? 250m : null,
        isCovered ? false : null,
        isCovered ? 0m : null,
        isCovered,
        true,
        isCovered
            ? [new BudgetConversionSnapshot(
                "USD",
                50m,
                250m,
                5m,
                new DateOnly(2026, 8, 1),
                ExchangeRateSource.Manual,
                null)]
            : []);

    private sealed class StubBudgetReader(
        IReadOnlyCollection<BudgetSnapshot> budgets,
        BudgetSnapshot? budget = null) : IBudgetReader, IBudgetConsumptionReader
    {
        public BudgetConsumptionResult ConsumptionResult { get; init; } = new(
            Consumption(),
            BudgetConsumptionOutcome.Succeeded);
        public Guid? UserId { get; private set; }
        public Guid? BudgetId { get; private set; }
        public bool IncludeDeleted { get; private set; }
        public DateOnly? AsOf { get; private set; }

        public Task<IReadOnlyCollection<BudgetSnapshot>> ListAsync(
            Guid userId,
            bool includeDeleted,
            DateOnly asOf,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            IncludeDeleted = includeDeleted;
            AsOf = asOf;
            return Task.FromResult(budgets);
        }

        public Task<BudgetSnapshot?> FindByIdAsync(
            Guid userId,
            Guid id,
            bool includeDeleted,
            DateOnly asOf,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            BudgetId = id;
            IncludeDeleted = includeDeleted;
            AsOf = asOf;
            return Task.FromResult(budget);
        }

        public Task<BudgetConsumptionResult> GetConsumptionAsync(
            Guid userId,
            Guid id,
            DateOnly periodDate,
            CancellationToken cancellationToken)
        {
            UserId = userId;
            BudgetId = id;
            AsOf = periodDate;
            return Task.FromResult(ConsumptionResult);
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
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
