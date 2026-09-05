using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class MaterializeRecurringTransactionsCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedRules_WhenMaterialized_ThenPerRuleResultsReturn()
    {
        var profile = Profile();
        var ruleId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var materializer = new StubMaterializer(new RecurringMaterializationResult([
            new RecurringRuleMaterializationResult(ruleId, [
                new RecurringOccurrenceMaterializationResult(
                    new DateOnly(2026, 9, 5), transactionId, true)
            ], false)
        ]));

        var result = await Handler(profile, materializer).HandleAsync(new());

        Assert.True(result.Success);
        Assert.Equal(1, result.Data?.CreatedCount);
        Assert.Equal(1, result.Data?.PossibleDuplicateCount);
        Assert.Equal(transactionId, result.Data?.Rules.Single().Occurrences.Single().TransactionId);
        Assert.Equal(profile.Id, materializer.Run?.UserId);
    }

    [UnitFact]
    public async Task GivenSkippedAndFailedRules_WhenMaterialized_ThenReasonsReturn()
    {
        var materializer = new StubMaterializer(new RecurringMaterializationResult([
            new RecurringRuleMaterializationResult(
                Guid.NewGuid(), [], true, RecurringMaterializationSkipReason.CategoryDeleted),
            new RecurringRuleMaterializationResult(Guid.NewGuid(), [
                new RecurringOccurrenceMaterializationResult(
                    new DateOnly(2026, 9, 5), null, false, RecurringTransactionMessages.OccurrenceFailed)
            ], false)
        ]));

        var result = await Handler(Profile(), materializer).HandleAsync(new());

        Assert.Equal("CategoryDeleted", result.Data?.Rules.First().SkipReason);
        Assert.Equal(
            RecurringTransactionMessages.OccurrenceFailed,
            result.Data?.Rules.Last().Occurrences.Single().Error);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenMaterialized_ThenStoreIsNotCalled()
    {
        var materializer = new StubMaterializer(new RecurringMaterializationResult([]));

        var result = await Handler(null, materializer).HandleAsync(new());

        Assert.Contains(RecurringTransactionMessages.ProfileNotFound, result.Errors);
        Assert.Null(materializer.Run);
    }

    [UnitFact]
    public async Task GivenInvalidCommand_WhenMaterialized_ThenStoreIsNotCalled()
    {
        var materializer = new StubMaterializer(new RecurringMaterializationResult([]));

        var result = await Handler(Profile(), materializer).HandleAsync(new()
        {
            OwnerId = Guid.NewGuid()
        });

        Assert.Contains(RecurringTransactionMessages.OwnerImmutable, result.Errors);
        Assert.Null(materializer.Run);
    }

    private static MaterializeRecurringTransactionsCommandHandler Handler(
        UserProfileSnapshot? profile,
        IRecurringTransactionMaterializer materializer) => new(
        new MaterializeRecurringTransactionsCommandValidator(),
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfiles(profile),
        materializer,
        new FixedTimeProvider(Now));

    private sealed class StubMaterializer(RecurringMaterializationResult result)
        : IRecurringTransactionMaterializer
    {
        public RecurringMaterializationRun? Run { get; private set; }

        public Task<RecurringMaterializationResult> MaterializeAsync(
            RecurringMaterializationRun run,
            CancellationToken cancellationToken)
        {
            Run = run;
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
