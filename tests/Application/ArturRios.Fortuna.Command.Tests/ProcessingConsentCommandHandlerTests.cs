using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Moq;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class ProcessingConsentCommandHandlerTests
{
    private const string CurrentVersion = "2.0";
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserProfileSnapshot Profile = new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    [UnitTheory]
    [InlineData("external-data-processing")]
    [InlineData("  External-Data-Processing  ")]
    public async Task GivenKnownPurposeInAnyCase_WhenGranted_ThenTheCurrentVersionIsRecordedNow(string purpose)
    {
        var consentId = Guid.NewGuid();
        var store = new Mock<IProcessingConsentStore>();
        store.Setup(item => item.GrantAsync(
                Profile.Id, ProcessingConsentPurpose.ExternalDataProcessing, CurrentVersion, Now, CancellationToken.None))
            .ReturnsAsync(new ProcessingConsentSnapshot(
                consentId, ProcessingConsentPurpose.ExternalDataProcessing, CurrentVersion, Now, Now));

        // The command's own version is not what gets recorded: a grant is always of the version in force.
        var result = await GrantHandler(Profile, store.Object).HandleAsync(
            new GrantProcessingConsentCommand { Purpose = purpose, Version = "1.0" });

        Assert.True(result.Success);
        Assert.Contains(ProcessingConsentMessages.GrantedSuccessfully, result.Messages);
        Assert.Equal(consentId, result.Data?.Id);
        Assert.Equal(ProcessingConsentOptions.ExternalDataProcessingName, result.Data?.Purpose);
        Assert.Equal(CurrentVersion, result.Data?.Version);
        Assert.Equal(Now, result.Data?.GrantedAt);
        Assert.True(result.Data?.IsCurrent);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("marketing")]
    public async Task GivenUnknownPurpose_WhenGranted_ThenItIsRejectedBeforeTheProfileIsResolved(string purpose)
    {
        var resolver = new Mock<ICurrentProfileResolver>(MockBehavior.Strict);
        var store = new Mock<IProcessingConsentStore>(MockBehavior.Strict);
        var handler = new GrantProcessingConsentCommandHandler(
            resolver.Object, store.Object, new ProcessingConsentOptions(CurrentVersion), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GrantProcessingConsentCommand { Purpose = purpose });

        Assert.Equal([ProcessingConsentMessages.UnknownPurpose], result.Errors);
        resolver.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenGranted_ThenProfileNotFoundAndNothingIsRecorded()
    {
        var store = new Mock<IProcessingConsentStore>(MockBehavior.Strict);

        var result = await GrantHandler(null, store.Object).HandleAsync(
            new GrantProcessingConsentCommand { Purpose = ProcessingConsentOptions.ExternalDataProcessingName });

        Assert.Equal([ProcessingConsentMessages.ProfileNotFound], result.Errors);
        store.VerifyNoOtherCalls();
    }

    [UnitFact]
    public async Task GivenGrantedConsent_WhenWithdrawn_ThenWhatTheWithdrawalStoppedIsReturned()
    {
        var store = new Mock<IProcessingConsentStore>();
        store.Setup(item => item.WithdrawAsync(
                Profile.Id, ProcessingConsentPurpose.ExternalDataProcessing, Now, CancellationToken.None))
            .ReturnsAsync(new ProcessingConsentWithdrawal(true, 2, 3));

        var result = await WithdrawHandler(Profile, store.Object).HandleAsync(
            new WithdrawProcessingConsentCommand { Purpose = " EXTERNAL-DATA-PROCESSING " });

        Assert.True(result.Success);
        Assert.Contains(ProcessingConsentMessages.WithdrawnSuccessfully, result.Messages);
        Assert.Equal(ProcessingConsentOptions.ExternalDataProcessingName, result.Data?.Purpose);
        Assert.Equal(2, result.Data?.RevokedConnections);
        Assert.Equal(3, result.Data?.StoppedSynchronizations);
    }

    [UnitFact]
    public async Task GivenNoConsentToWithdraw_WhenWithdrawn_ThenNotFoundIsReturnedWithoutData()
    {
        var store = new Mock<IProcessingConsentStore>();
        store.Setup(item => item.WithdrawAsync(
                It.IsAny<Guid>(), It.IsAny<ProcessingConsentPurpose>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessingConsentWithdrawal(false, 0, 0));

        var result = await WithdrawHandler(Profile, store.Object).HandleAsync(
            new WithdrawProcessingConsentCommand { Purpose = ProcessingConsentOptions.ExternalDataProcessingName });

        Assert.Equal([ProcessingConsentMessages.NotFound], result.Errors);
        Assert.Null(result.Data);
    }

    [UnitFact]
    public async Task GivenUnknownPurpose_WhenWithdrawn_ThenItIsRejectedBeforeTheProfileIsResolved()
    {
        var resolver = new Mock<ICurrentProfileResolver>(MockBehavior.Strict);
        var store = new Mock<IProcessingConsentStore>(MockBehavior.Strict);
        var handler = new WithdrawProcessingConsentCommandHandler(resolver.Object, store.Object, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new WithdrawProcessingConsentCommand { Purpose = "marketing" });

        Assert.Equal([ProcessingConsentMessages.UnknownPurpose], result.Errors);
        resolver.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenWithdrawn_ThenProfileNotFoundAndNothingIsWithdrawn()
    {
        var store = new Mock<IProcessingConsentStore>(MockBehavior.Strict);

        var result = await WithdrawHandler(null, store.Object).HandleAsync(
            new WithdrawProcessingConsentCommand { Purpose = ProcessingConsentOptions.ExternalDataProcessingName });

        Assert.Equal([ProcessingConsentMessages.ProfileNotFound], result.Errors);
        store.VerifyNoOtherCalls();
    }

    private static GrantProcessingConsentCommandHandler GrantHandler(
        UserProfileSnapshot? profile,
        IProcessingConsentStore store) => new(
        Resolver(profile), store, new ProcessingConsentOptions(CurrentVersion), new FixedTimeProvider(Now));

    private static WithdrawProcessingConsentCommandHandler WithdrawHandler(
        UserProfileSnapshot? profile,
        IProcessingConsentStore store) => new(Resolver(profile), store, new FixedTimeProvider(Now));

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
