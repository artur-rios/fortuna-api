using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Moq;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class InstallmentPlanLifecycleCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserProfileSnapshot Profile = new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    [UnitFact]
    public async Task GivenOwnedPlan_WhenDeleted_ThenTheStoreSoftDeletesItForTheOwnerNow()
    {
        var planId = Guid.NewGuid();
        var store = new Mock<IInstallmentPlanLifecycleStore>();
        store.Setup(item => item.SoftDeleteAsync(Profile.Id, planId, Now, CancellationToken.None))
            .ReturnsAsync(new InstallmentPlanLifecycleResult(planId, InstallmentPlanLifecycleOutcome.Succeeded));

        var result = await DeleteHandler(Profile, store.Object).HandleAsync(new DeleteInstallmentPlanCommand { Id = planId });

        Assert.True(result.Success);
        Assert.Equal(planId, result.Data?.Id);
        Assert.Contains(InstallmentPlanMessages.DeletedSuccessfully, result.Messages);
        store.Verify(item => item.RestoreAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [UnitFact]
    public async Task GivenDeletedPlan_WhenRestored_ThenTheStoreRestoresItForTheOwnerNow()
    {
        var planId = Guid.NewGuid();
        var store = new Mock<IInstallmentPlanLifecycleStore>();
        store.Setup(item => item.RestoreAsync(Profile.Id, planId, Now, CancellationToken.None))
            .ReturnsAsync(new InstallmentPlanLifecycleResult(planId, InstallmentPlanLifecycleOutcome.Succeeded));

        var result = await RestoreHandler(Profile, store.Object).HandleAsync(new RestoreInstallmentPlanCommand { Id = planId });

        Assert.True(result.Success);
        Assert.Equal(planId, result.Data?.Id);
        Assert.Contains(InstallmentPlanMessages.RestoredSuccessfully, result.Messages);
        store.Verify(item => item.SoftDeleteAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [UnitTheory]
    [InlineData(false, InstallmentPlanLifecycleOutcome.NotFound, InstallmentPlanMessages.NotFound)]
    [InlineData(false, InstallmentPlanLifecycleOutcome.SettledStatementFrozen, InstallmentPlanMessages.SettledStatementFrozen)]
    [InlineData(true, InstallmentPlanLifecycleOutcome.NotFound, InstallmentPlanMessages.NotFound)]
    [InlineData(true, InstallmentPlanLifecycleOutcome.RestoreRequiresSoftDeletion,
        InstallmentPlanMessages.RestoreRequiresSoftDeletion)]
    [InlineData(true, InstallmentPlanLifecycleOutcome.SettledStatementFrozen, InstallmentPlanMessages.SettledStatementFrozen)]
    public async Task GivenLifecycleRefusal_WhenHandled_ThenItsCanonicalErrorIsReturnedWithoutData(
        bool restore,
        InstallmentPlanLifecycleOutcome outcome,
        string expected)
    {
        var store = StoreAnswering(new InstallmentPlanLifecycleResult(null, outcome));

        var result = restore
            ? await RestoreHandler(Profile, store).HandleAsync(new RestoreInstallmentPlanCommand { Id = Guid.NewGuid() })
            : await DeleteHandler(Profile, store).HandleAsync(new DeleteInstallmentPlanCommand { Id = Guid.NewGuid() });

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Equal([expected], result.Errors);
    }

    [UnitTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenMissingProfile_WhenHandled_ThenProfileNotFoundAndTheStoreIsNotCalled(bool restore)
    {
        var store = new Mock<IInstallmentPlanLifecycleStore>(MockBehavior.Strict);

        var result = restore
            ? await RestoreHandler(null, store.Object).HandleAsync(new RestoreInstallmentPlanCommand { Id = Guid.NewGuid() })
            : await DeleteHandler(null, store.Object).HandleAsync(new DeleteInstallmentPlanCommand { Id = Guid.NewGuid() });

        Assert.Equal([InstallmentPlanMessages.ProfileNotFound], result.Errors);
        store.VerifyNoOtherCalls();
    }

    [UnitFact]
    public async Task GivenAnOutcomeTheHandlerDoesNotKnow_WhenHandled_ThenItThrows()
    {
        var store = StoreAnswering(new InstallmentPlanLifecycleResult(null, (InstallmentPlanLifecycleOutcome)99));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            DeleteHandler(Profile, store).HandleAsync(new DeleteInstallmentPlanCommand { Id = Guid.NewGuid() }));
    }

    private static IInstallmentPlanLifecycleStore StoreAnswering(InstallmentPlanLifecycleResult result)
    {
        var store = new Mock<IInstallmentPlanLifecycleStore>();
        store.Setup(item => item.SoftDeleteAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        store.Setup(item => item.RestoreAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        return store.Object;
    }

    private static DeleteInstallmentPlanCommandHandler DeleteHandler(
        UserProfileSnapshot? profile,
        IInstallmentPlanLifecycleStore store) => new(Resolver(profile), store, new FixedTimeProvider(Now));

    private static RestoreInstallmentPlanCommandHandler RestoreHandler(
        UserProfileSnapshot? profile,
        IInstallmentPlanLifecycleStore store) => new(Resolver(profile), store, new FixedTimeProvider(Now));

    private static ICurrentProfileResolver Resolver(UserProfileSnapshot? profile)
    {
        var resolver = new Mock<ICurrentProfileResolver>();
        resolver.Setup(item => item.ResolveAsync()).ReturnsAsync(profile);

        return resolver.Object;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
