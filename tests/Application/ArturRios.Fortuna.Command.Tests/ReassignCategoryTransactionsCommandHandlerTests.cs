using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class ReassignCategoryTransactionsCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 23, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidRequest_WhenReassigned_ThenCountAndSelectionAreReturned()
    {
        var subject = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var store = new StubCategoryTransactionReassigner(
            new CategoryTransactionReassignmentResult(
                7,
                CategoryTransactionReassignmentOutcome.Succeeded));

        var result = await Handler(subject, Profile(userId, subject), store).HandleAsync(new()
        {
            Id = sourceId,
            TargetCategoryId = targetId,
            IncludeDescendants = true
        });

        Assert.True(result.Success);
        Assert.Equal(sourceId, result.Data!.Id);
        Assert.Equal(targetId, result.Data.TargetCategoryId);
        Assert.True(result.Data.IncludeDescendants);
        Assert.Equal(7, result.Data.ReassignedCount);
        Assert.Equal(userId, store.Reassignment!.UserId);
        Assert.Equal(sourceId, store.Reassignment.SourceCategoryId);
        Assert.Equal(targetId, store.Reassignment.TargetCategoryId);
        Assert.True(store.Reassignment.IncludeDescendants);
        Assert.Equal(Now, store.Reassignment.ChangedAt);
        Assert.Contains(CategoryMessages.TransactionsReassignedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(CategoryTransactionReassignmentOutcome.CategoryNotFound,
        CategoryMessages.NotFound)]
    [InlineData(CategoryTransactionReassignmentOutcome.SameCategory,
        CategoryMessages.SourceAndTargetMustDiffer)]
    public async Task GivenStoreRefusal_WhenReassigned_ThenExpectedErrorIsReturned(
        CategoryTransactionReassignmentOutcome outcome,
        string expected)
    {
        var subject = Guid.NewGuid();
        var store = new StubCategoryTransactionReassigner(
            new CategoryTransactionReassignmentResult(0, outcome));

        var result = await Handler(subject, Profile(Guid.NewGuid(), subject), store)
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenReassigned_ThenNotFoundIsReturned()
    {
        var store = new StubCategoryTransactionReassigner(
            new CategoryTransactionReassignmentResult(
                0,
                CategoryTransactionReassignmentOutcome.Succeeded));

        var result = await Handler(Guid.NewGuid(), null, store)
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(CategoryMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Reassignment);
    }

    [UnitFact]
    public async Task GivenSameCategory_WhenReassigned_ThenNothingIsStored()
    {
        var id = Guid.NewGuid();
        var store = new StubCategoryTransactionReassigner(
            new CategoryTransactionReassignmentResult(
                0,
                CategoryTransactionReassignmentOutcome.Succeeded));

        var result = await Handler(Guid.NewGuid(), null, store).HandleAsync(new()
        {
            Id = id,
            TargetCategoryId = id
        });

        Assert.False(result.Success);
        Assert.Contains(CategoryMessages.SourceAndTargetMustDiffer, result.Errors);
        Assert.Null(store.Reassignment);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenReassigned_ThenProfileIsResolvedByPublicId()
    {
        var userId = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile(userId, null));
        var store = new StubCategoryTransactionReassigner(
            new CategoryTransactionReassignmentResult(
                0,
                CategoryTransactionReassignmentOutcome.Succeeded));
        var handler = new ReassignCategoryTransactionsCommandHandler(
            new ReassignCategoryTransactionsCommandValidator(),
            new StubActorAccessor(new RequestActor(userId, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static ReassignCategoryTransactionsCommandHandler Handler(
        Guid subject,
        UserProfileSnapshot? profile,
        ICategoryTransactionReassigner store) => new(
            new ReassignCategoryTransactionsCommandValidator(),
            new StubActorAccessor(new RequestActor(subject, 3, null, [])),
            new StubUserProfileReader(profile),
            store,
            new FixedTimeProvider(Now));

    private static ReassignCategoryTransactionsCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        TargetCategoryId = Guid.NewGuid()
    };

    private static UserProfileSnapshot Profile(Guid id, Guid? subject) => new(
        id,
        subject,
        "Account Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubCategoryTransactionReassigner(
        CategoryTransactionReassignmentResult result) : ICategoryTransactionReassigner
    {
        public CategoryTransactionReassignment? Reassignment { get; private set; }

        public Task<CategoryTransactionReassignmentResult> ReassignAsync(
            CategoryTransactionReassignment reassignment,
            CancellationToken cancellationToken)
        {
            Reassignment = reassignment;
            return Task.FromResult(result);
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

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
