using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class UserProfileProvisioningMiddlewareTests
{
    [UnitTheory]
    [InlineData("/api/me/erasure", true)]
    [InlineData("/api/me/erasure/", true)]
    [InlineData("/API/ME/ERASURE", true)]
    [InlineData("/Api/Me/Erasure/", true)]
    [InlineData("/api/me", false)]
    [InlineData("/api/me/erasures", false)]
    [InlineData("/api/me/erasure/extra", false)]
    [InlineData("/api/me/data-export", false)]
    public void GivenRequestPath_WhenCheckingForErasure_ThenCaseAndTrailingSlashAreIgnored(
        string path,
        bool expected)
    {
        Assert.Equal(expected, UserProfileProvisioningMiddleware.IsErasurePath(new PathString(path)));
    }

    [UnitTheory]
    [InlineData("/api/me/erasure/", 0)]
    [InlineData("/API/me/Erasure", 0)]
    [InlineData("/api/me", 1)]
    public async Task GivenHeimdallActor_WhenRequestPasses_ThenProfileIsProvisionedExceptForErasure(
        string path,
        int expectedProvisioning)
    {
        var provisioner = new CountingProvisioner();
        var nextCalled = false;
        var middleware = new UserProfileProvisioningMiddleware(_ =>
        {
            nextCalled = true;

            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var actor = new RequestActor(Guid.NewGuid(), (int)HeimdallRoles.User, Guid.NewGuid(), [])
        {
            DisplayName = "Erasing User"
        };

        await middleware.InvokeAsync(context, new StubActorAccessor(actor), provisioner, Cache());

        Assert.True(nextCalled);
        Assert.Equal(expectedProvisioning, provisioner.Calls);
    }

    [UnitFact]
    public async Task GivenProvisionedSubject_WhenItRequestsAgain_ThenProfileIsNotLookedUpAgain()
    {
        var provisioner = new CountingProvisioner();
        var cache = Cache();
        var actor = new RequestActor(Guid.NewGuid(), (int)HeimdallRoles.User, Guid.NewGuid(), [])
        {
            DisplayName = "Returning User"
        };
        var middleware = new UserProfileProvisioningMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(Context("/api/me"), new StubActorAccessor(actor), provisioner, cache);
        await middleware.InvokeAsync(Context("/api/me"), new StubActorAccessor(actor), provisioner, cache);

        Assert.Equal(1, provisioner.Calls);
    }

    [UnitFact]
    public async Task GivenErasedSubject_WhenItRequestsAgain_ThenProfileIsProvisionedAgain()
    {
        var provisioner = new CountingProvisioner();
        var cache = Cache();
        var actor = new RequestActor(Guid.NewGuid(), (int)HeimdallRoles.User, Guid.NewGuid(), [])
        {
            DisplayName = "Erased User"
        };
        var middleware = new UserProfileProvisioningMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(Context("/api/me"), new StubActorAccessor(actor), provisioner, cache);
        cache.Forget(actor.SubjectId);
        await middleware.InvokeAsync(Context("/api/me"), new StubActorAccessor(actor), provisioner, cache);
        await middleware.InvokeAsync(Context("/api/me"), new StubActorAccessor(actor), provisioner, cache);

        Assert.Equal(3, provisioner.Calls);
    }

    private static MemoryProvisionedProfileCache Cache() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    private static DefaultHttpContext Context(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        return context;
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor { get; } = actor;
    }

    private sealed class CountingProvisioner : IUserProfileProvisioner
    {
        public int Calls { get; private set; }

        public Task<UserProfileSnapshot> GetOrCreateAsync(
            Guid externalSubject,
            string displayName,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new UserProfileSnapshot(
                Guid.NewGuid(), externalSubject, displayName, "BRL", false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }
}
