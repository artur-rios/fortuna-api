using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class GetMyProfileQueryHandlerTests
{
    private sealed class StubReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActorAccessor(RequestActor actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private static CurrentProfileResolver Resolver(Guid subject, UserProfileSnapshot? profile) => new(
        new StubActorAccessor(new RequestActor(subject, 3, null, [])),
        new StubReader(profile));

    [UnitFact]
    public async Task GivenProvisionedProfile_WhenProfileIsQueried_ThenPublicProfileIsReturned()
    {
        var subject = Guid.NewGuid();
        var profile = new UserProfileSnapshot(
            Guid.NewGuid(), subject, "Ada Lovelace", "BRL", false,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var handler = new GetMyProfileQueryHandler(Resolver(subject, profile));

        var result = await handler.HandleAsync(new GetMyProfileQuery());

        Assert.True(result.Success);
        Assert.Equal(profile.Id, result.Data!.Id);
        Assert.Equal(profile.DisplayName, result.Data.DisplayName);
        Assert.Equal(profile.DisplayCurrency, result.Data.DisplayCurrency);
        Assert.Contains(UserProfileMessages.ProfileRetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenProfileMissing_WhenProfileIsQueried_ThenNotFoundErrorIsReturned()
    {
        var handler = new GetMyProfileQueryHandler(Resolver(Guid.NewGuid(), null));

        var result = await handler.HandleAsync(new GetMyProfileQuery());

        Assert.False(result.Success);
        Assert.Contains(UserProfileMessages.ProfileNotFound, result.Errors);
    }
}
