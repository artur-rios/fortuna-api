using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class EraseUserCommandHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ExternalSubject = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-09T22:00:00Z");

    [UnitFact]
    public async Task GivenConfirmedOwner_WhenErasingSelf_ThenEveryCategoryCountIsReturned()
    {
        var store = new StubErasureStore(new UserErasureResult(
            Guid.NewGuid(),
            new Dictionary<string, int> { ["financialRecords"] = 7, ["profiles"] = 1 },
            2));
        var handler = Handler(
            new RequestActor(ExternalSubject, (int)HeimdallRoles.User, null, []),
            Profile(),
            store);

        var result = await handler.HandleAsync(new EraseUserCommand
        {
            Confirmation = "ERASE",
            IsSelfService = true
        });

        Assert.True(result.Success);
        Assert.Equal(7, result.Data!.Erased["financialRecords"]);
        Assert.Equal(1, result.Data.Erased["profiles"]);
        Assert.Equal(2, result.Data.RevokedConnections);
        Assert.True(result.Data.Irreversible);
        Assert.Equal(UserId, store.ErasedUserId);
        Assert.Contains(UserErasureMessages.ErasedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenLocalOwner_WhenErasingSelf_ThenPublicProfileIdentityIsUsed()
    {
        var profiles = new StubProfiles(Profile());
        var handler = Handler(
            new RequestActor(UserId, (int)HeimdallRoles.User, null, []) { IsLocal = true },
            profiles,
            new StubErasureStore(Result()));

        var result = await handler.HandleAsync(ConfirmedSelf());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    [UnitFact]
    public async Task GivenInstanceAdministrator_WhenErasingTarget_ThenOnlyTargetPublicIdIsResolved()
    {
        var profiles = new StubProfiles(Profile());
        var handler = Handler(
            new RequestActor(Guid.NewGuid(), (int)HeimdallRoles.SystemAdmin, null, []),
            profiles,
            new StubErasureStore(Result()));

        var result = await handler.HandleAsync(new EraseUserCommand
        {
            Confirmation = "ERASE",
            UserId = UserId
        });

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
        Assert.False(profiles.ExternalSubjectLookupUsed);
    }

    [UnitFact]
    public async Task GivenMissingOrMismatchedConfirmation_WhenErasing_ThenNothingIsErased()
    {
        var store = new StubErasureStore(Result());
        var handler = Handler(
            new RequestActor(ExternalSubject, (int)HeimdallRoles.User, null, []),
            Profile(),
            store);

        var missing = await handler.HandleAsync(new EraseUserCommand { IsSelfService = true });
        var mismatched = await handler.HandleAsync(new EraseUserCommand
        {
            Confirmation = "erase",
            IsSelfService = true
        });

        Assert.All(new[] { missing, mismatched }, result =>
        {
            Assert.False(result.Success);
            Assert.Contains(UserErasureMessages.ConfirmationInvalid, result.Errors);
        });
        Assert.Null(store.ErasedUserId);
    }

    [UnitFact]
    public async Task GivenNonAdministratorTargetingAnotherUser_WhenErasing_ThenNotFoundStoresNothing()
    {
        var store = new StubErasureStore(Result());
        var handler = Handler(
            new RequestActor(ExternalSubject, (int)HeimdallRoles.User, null, []),
            Profile(),
            store);

        var result = await handler.HandleAsync(new EraseUserCommand
        {
            Confirmation = "ERASE",
            UserId = UserId
        });

        Assert.False(result.Success);
        Assert.Contains(UserErasureMessages.UserNotFound, result.Errors);
        Assert.Null(store.ErasedUserId);
    }

    [UnitFact]
    public async Task GivenMissingOrAlreadyErasedUser_WhenErasing_ThenNotFoundIsReturned()
    {
        var missingProfile = await Handler(
            new RequestActor(ExternalSubject, (int)HeimdallRoles.User, null, []),
            (UserProfileSnapshot?)null,
            new StubErasureStore(Result())).HandleAsync(ConfirmedSelf());
        var disappeared = await Handler(
            new RequestActor(ExternalSubject, (int)HeimdallRoles.User, null, []),
            Profile(),
            new StubErasureStore(null)).HandleAsync(ConfirmedSelf());

        Assert.All(new[] { missingProfile, disappeared }, result =>
        {
            Assert.False(result.Success);
            Assert.Contains(UserErasureMessages.UserNotFound, result.Errors);
        });
    }

    private static EraseUserCommand ConfirmedSelf() => new()
    {
        Confirmation = "ERASE",
        IsSelfService = true
    };

    private static UserErasureResult Result() => new(
        Guid.NewGuid(),
        new Dictionary<string, int> { ["profiles"] = 1 },
        0);

    private static UserProfileSnapshot Profile() => new(
        UserId,
        ExternalSubject,
        "Erasure User",
        "BRL",
        false,
        Now,
        Now);

    private static EraseUserCommandHandler Handler(
        RequestActor actor,
        UserProfileSnapshot? profile,
        StubErasureStore store) => Handler(actor, new StubProfiles(profile), store);

    private static EraseUserCommandHandler Handler(
        RequestActor actor,
        StubProfiles profiles,
        StubErasureStore store) => new(
        new EraseUserCommandValidator(),
        new StubActor(actor),
        profiles,
        store,
        new FixedTimeProvider(Now));

    private sealed class StubActor(RequestActor actor) : IRequestActorAccessor
    {
        public RequestActor Actor => actor;
    }

    private sealed class StubProfiles(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public bool PublicIdLookupUsed { get; private set; }
        public bool ExternalSubjectLookupUsed { get; private set; }

        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken)
        {
            ExternalSubjectLookupUsed = true;
            return Task.FromResult(profile);
        }

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken)
        {
            PublicIdLookupUsed = true;
            return Task.FromResult(profile);
        }
    }

    private sealed class StubErasureStore(UserErasureResult? result) : IUserErasureStore
    {
        public Guid? ErasedUserId { get; private set; }

        public Task<UserErasureResult?> EraseAsync(
            Guid userId,
            DateTimeOffset erasedAt,
            CancellationToken cancellationToken)
        {
            ErasedUserId = userId;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
