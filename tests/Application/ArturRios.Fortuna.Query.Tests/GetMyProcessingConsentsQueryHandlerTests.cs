using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;
using Moq;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class GetMyProcessingConsentsQueryHandlerTests
{
    private const string CurrentVersion = "2.0";
    private static readonly DateTimeOffset GrantedAt = new(2026, 8, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly UserProfileSnapshot Profile = new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, GrantedAt, GrantedAt);

    [UnitFact]
    public async Task GivenConsentAtTheCurrentVersion_WhenQueried_ThenItIsReportedCurrent()
    {
        var result = await Handler(Profile, Consent(CurrentVersion)).HandleAsync(new GetMyProcessingConsentsQuery());

        Assert.True(result.Success);
        Assert.Contains(ProcessingConsentMessages.RetrievedSuccessfully, result.Messages);
        var state = Assert.Single(result.Data!.Consents);
        Assert.Equal(ProcessingConsentOptions.ExternalDataProcessingName, state.Purpose);
        Assert.Equal(CurrentVersion, state.CurrentVersion);
        Assert.Equal(CurrentVersion, state.GrantedVersion);
        Assert.Equal(GrantedAt, state.GrantedAt);
        Assert.True(state.IsCurrent);
    }

    [UnitFact]
    public async Task GivenConsentAtAnEarlierVersion_WhenQueried_ThenTheGrantShowsButIsNotCurrent()
    {
        var result = await Handler(Profile, Consent("1.0")).HandleAsync(new GetMyProcessingConsentsQuery());

        var state = Assert.Single(result.Data!.Consents);
        Assert.Equal("1.0", state.GrantedVersion);
        Assert.Equal(CurrentVersion, state.CurrentVersion);
        Assert.False(state.IsCurrent);
    }

    [UnitFact]
    public async Task GivenNoConsent_WhenQueried_ThenEveryPurposeIsListedAsNeverGranted()
    {
        var result = await Handler(Profile).HandleAsync(new GetMyProcessingConsentsQuery());

        Assert.Equal(Enum.GetValues<ProcessingConsentPurpose>().Length, result.Data!.Consents.Count);
        Assert.All(result.Data.Consents, state =>
        {
            Assert.Equal(CurrentVersion, state.CurrentVersion);
            Assert.Null(state.GrantedVersion);
            Assert.Null(state.GrantedAt);
            Assert.False(state.IsCurrent);
        });
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenQueried_ThenProfileNotFoundAndNoConsentIsRead()
    {
        var reader = new Mock<IProcessingConsentReader>(MockBehavior.Strict);
        var resolver = new Mock<ICurrentProfileResolver>();
        resolver.Setup(item => item.ResolveAsync()).ReturnsAsync((UserProfileSnapshot?)null);
        var handler = new GetMyProcessingConsentsQueryHandler(
            resolver.Object, reader.Object, new ProcessingConsentOptions(CurrentVersion));

        var result = await handler.HandleAsync(new GetMyProcessingConsentsQuery());

        Assert.Equal([ProcessingConsentMessages.ProfileNotFound], result.Errors);
        Assert.Null(result.Data);
        reader.VerifyNoOtherCalls();
    }

    private static ProcessingConsentSnapshot Consent(string version) => new(
        Guid.NewGuid(), ProcessingConsentPurpose.ExternalDataProcessing, version, GrantedAt, GrantedAt);

    private static GetMyProcessingConsentsQueryHandler Handler(
        UserProfileSnapshot profile,
        params ProcessingConsentSnapshot[] consents)
    {
        var resolver = new Mock<ICurrentProfileResolver>();
        resolver.Setup(item => item.ResolveAsync()).ReturnsAsync(profile);
        var reader = new Mock<IProcessingConsentReader>();
        reader.Setup(item => item.ListAsync(profile.Id, CancellationToken.None)).ReturnsAsync(consents);

        return new GetMyProcessingConsentsQueryHandler(
            resolver.Object, reader.Object, new ProcessingConsentOptions(CurrentVersion));
    }
}
