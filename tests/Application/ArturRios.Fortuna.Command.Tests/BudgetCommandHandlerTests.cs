using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Planning;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class BudgetCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidBudget_WhenCreated_ThenSnapshotAndConsumptionAreReturned()
    {
        var profile = Profile();
        var snapshot = Snapshot();
        var store = new StubBudgetStore
        {
            Result = new BudgetMutationResult(snapshot, BudgetMutationOutcome.Succeeded)
        };
        var handler = new CreateBudgetCommandHandler(
            new CreateBudgetCommandValidator(),
            Actor(profile),
            new StubProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateBudgetCommand
        {
            Amount = 500m,
            CurrencyCode = "brl",
            PeriodType = BudgetPeriodType.Monthly,
            PeriodStart = new DateOnly(2026, 9, 1),
            CategoryIds = [snapshot.Categories.Single().Id]
        });

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(125m, result.Data?.CurrentPeriod.Spent);
        Assert.Equal(profile.Id, store.Creation?.UserId);
        Assert.Equal("BRL", store.Creation?.CurrencyCode);
        Assert.True(store.Creation?.IncludeDescendants);
        Assert.Equal(Now, store.Creation?.CreatedAt);
        Assert.Contains(BudgetMessages.CreatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenInvalidBudget_WhenCreated_ThenStoreIsNotCalled()
    {
        var store = new StubBudgetStore();
        var handler = new CreateBudgetCommandHandler(
            new CreateBudgetCommandValidator(),
            Actor(Profile()),
            new StubProfileReader(Profile()),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateBudgetCommand());

        Assert.False(result.Success);
        Assert.Null(store.Creation);
        Assert.Contains(BudgetMessages.AmountMustBePositive, result.Errors);
    }

    [UnitTheory]
    [InlineData(BudgetMutationOutcome.NotFound, BudgetMessages.NotFound)]
    [InlineData(BudgetMutationOutcome.CategoryNotFound, BudgetMessages.CategoryNotFound)]
    [InlineData(BudgetMutationOutcome.CurrencyNotFound, BudgetMessages.CurrencyNotSupported)]
    public async Task GivenRejectedUpdate_WhenHandled_ThenExpectedErrorIsReturned(
        BudgetMutationOutcome outcome,
        string expectedError)
    {
        var profile = Profile();
        var store = new StubBudgetStore
        {
            Result = new BudgetMutationResult(null, outcome)
        };
        var handler = new UpdateBudgetCommandHandler(
            new UpdateBudgetCommandValidator(),
            Actor(profile),
            new StubProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new UpdateBudgetCommand
        {
            Id = Guid.NewGuid(),
            Amount = 500m,
            CurrencyCode = "BRL",
            PeriodType = BudgetPeriodType.Quarterly,
            PeriodStart = new DateOnly(2026, 7, 1),
            CategoryIds = [Guid.NewGuid()],
            IncludeDescendants = false
        });

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Errors);
        Assert.Equal(Now, store.Update?.UpdatedAt);
    }

    [UnitFact]
    public async Task GivenBudget_WhenDeleted_ThenCurrentDateIsPassedToStore()
    {
        var profile = Profile();
        var snapshot = Snapshot(isDeleted: true);
        var store = new StubBudgetStore
        {
            Result = new BudgetMutationResult(snapshot, BudgetMutationOutcome.Succeeded)
        };
        var handler = new DeleteBudgetCommandHandler(
            Actor(profile),
            new StubProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new DeleteBudgetCommand { Id = snapshot.Id });

        Assert.True(result.Success);
        Assert.True(result.Data?.IsDeleted);
        Assert.Equal(Now, store.DeletedAt);
        Assert.Equal(new DateOnly(2026, 9, 6), store.AsOf);
        Assert.Contains(BudgetMessages.DeletedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenCreated_ThenStoreIsNotCalled()
    {
        var store = new StubBudgetStore();
        var handler = new CreateBudgetCommandHandler(
            new CreateBudgetCommandValidator(),
            Actor(null),
            new StubProfileReader(null),
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateBudgetCommand
        {
            Amount = 500m,
            CurrencyCode = "BRL",
            PeriodType = BudgetPeriodType.Monthly,
            PeriodStart = new DateOnly(2026, 9, 1),
            CategoryIds = [Guid.NewGuid()]
        });

        Assert.False(result.Success);
        Assert.Null(store.Creation);
        Assert.Contains(BudgetMessages.ProfileNotFound, result.Errors);
    }

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private static BudgetSnapshot Snapshot(bool isDeleted = false) => new(
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
        isDeleted,
        Now,
        Now);

    private sealed class StubBudgetStore : IBudgetStore, IBudgetUpdater, IBudgetLifecycleStore
    {
        public BudgetMutationResult Result { get; init; } = new(
            Snapshot(), BudgetMutationOutcome.Succeeded);
        public BudgetCreation? Creation { get; private set; }
        public BudgetUpdate? Update { get; private set; }
        public DateTimeOffset? DeletedAt { get; private set; }
        public DateOnly? AsOf { get; private set; }

        public Task<BudgetMutationResult> CreateAsync(
            BudgetCreation creation,
            CancellationToken cancellationToken)
        {
            Creation = creation;
            return Task.FromResult(Result);
        }

        public Task<BudgetMutationResult> UpdateAsync(
            BudgetUpdate update,
            CancellationToken cancellationToken)
        {
            Update = update;
            return Task.FromResult(Result);
        }

        public Task<BudgetMutationResult> SoftDeleteAsync(
            Guid userId,
            Guid id,
            DateTimeOffset changedAt,
            DateOnly asOf,
            CancellationToken cancellationToken)
        {
            DeletedAt = changedAt;
            AsOf = asOf;
            return Task.FromResult(Result);
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
