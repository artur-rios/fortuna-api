using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Shared.Tests;

public sealed class CurrentProfileResolverTests
{
    private static readonly UserProfileSnapshot Profile = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Ada",
        "BRL",
        false,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    [UnitFact]
    public async Task GivenHeimdallActor_WhenResolved_ThenProfileIsFoundByExternalSubject()
    {
        var subject = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile);

        var profile = await new CurrentProfileResolver(
                new StubActorAccessor(new RequestActor(subject, 3, null, [])),
                profiles)
            .ResolveAsync();

        Assert.Same(Profile, profile);
        Assert.Equal(subject, profiles.ExternalSubject);
        Assert.Null(profiles.PublicId);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenResolved_ThenProfileIsFoundByPublicId()
    {
        var subject = Guid.NewGuid();
        var profiles = new StubUserProfileReader(Profile);

        var profile = await new CurrentProfileResolver(
                new StubActorAccessor(new RequestActor(subject, 3, null, []) { IsLocal = true }),
                profiles)
            .ResolveAsync();

        Assert.Same(Profile, profile);
        Assert.Equal(subject, profiles.PublicId);
        Assert.Null(profiles.ExternalSubject);
    }

    [UnitFact]
    public async Task GivenNoActor_WhenResolved_ThenNoProfileIsLookedUp()
    {
        var profiles = new StubUserProfileReader(Profile);

        var profile = await new CurrentProfileResolver(new StubActorAccessor(null), profiles)
            .ResolveAsync();

        Assert.Null(profile);
        Assert.Null(profiles.PublicId);
        Assert.Null(profiles.ExternalSubject);
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class StubUserProfileReader(UserProfileSnapshot profile) : IUserProfileReader
    {
        public Guid? ExternalSubject { get; private set; }
        public Guid? PublicId { get; private set; }

        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken)
        {
            ExternalSubject = externalSubject;

            return Task.FromResult<UserProfileSnapshot?>(profile);
        }

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken)
        {
            PublicId = publicId;

            return Task.FromResult<UserProfileSnapshot?>(profile);
        }
    }
}
