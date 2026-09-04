using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Hashing;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RegenerateLocalAccountRecoveryCodesCommandHandlerTests
{
    private const string Secret = "correct-horse-battery-staple";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-03T15:00:00Z");

    [UnitFact]
    public async Task GivenLocalActorAndMatchingSecret_WhenRegenerating_ThenNewCodesAreReturnedOnce()
    {
        var actor = LocalActor();
        var credentials = Credentials(actor.SubjectId, Secret);
        var store = new StubStore(credentials, regenerated: true);
        var generator = new StubGenerator([
            new GeneratedRecoveryCode("AAAA-1111", [1, 2, 3]),
            new GeneratedRecoveryCode("BBBB-2222", [4, 5, 6])
        ]);

        var result = await Handler(actor, store, generator).HandleAsync(new()
        {
            Secret = Secret
        });

        Assert.True(result.Success);
        Assert.Equal(["AAAA-1111", "BBBB-2222"], result.Data!.RecoveryCodes);
        Assert.Equal(LocalAccountMessages.RecoveryWarning, result.Data.RecoveryWarning);
        Assert.Equal(1, generator.CallCount);
        Assert.NotNull(store.Regeneration);
        Assert.Equal(actor.SubjectId, store.Regeneration.UserId);
        Assert.Equal([[1, 2, 3], [4, 5, 6]], store.Regeneration.RecoveryCodeHashes);
        Assert.Equal(Now, store.Regeneration.RegeneratedAt);
    }

    [UnitFact]
    public async Task GivenWrongSecret_WhenRegenerating_ThenExistingCodesRemainUntouched()
    {
        var actor = LocalActor();
        var store = new StubStore(Credentials(actor.SubjectId, Secret), regenerated: true);
        var generator = new StubGenerator([]);

        var result = await Handler(actor, store, generator).HandleAsync(new()
        {
            Secret = "wrong-secret"
        });

        Assert.False(result.Success);
        Assert.Contains(LocalRecoveryCodeRegenerationMessages.InvalidSecret, result.Errors);
        Assert.Equal(0, generator.CallCount);
        Assert.Null(store.Regeneration);
    }

    [UnitFact]
    public async Task GivenHeimdallActor_WhenRegenerating_ThenLocalOnlyEndpointIsHidden()
    {
        var actor = LocalActor() with { IsLocal = false };
        var store = new StubStore(null, regenerated: true);

        var result = await Handler(actor, store, new StubGenerator([])).HandleAsync(new()
        {
            Secret = Secret
        });

        Assert.False(result.Success);
        Assert.Contains(LocalRecoveryCodeRegenerationMessages.LocalAccountOnly, result.Errors);
        Assert.Equal(0, store.FindCount);
    }

    [UnitFact]
    public async Task GivenGenerationFailure_WhenRegenerating_ThenStoreIsNeverAskedToReplaceCodes()
    {
        var actor = LocalActor();
        var store = new StubStore(Credentials(actor.SubjectId, Secret), regenerated: true);
        var generator = new ThrowingGenerator();
        var handler = Handler(actor, store, generator);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new()
        {
            Secret = Secret
        }));

        Assert.Null(store.Regeneration);
    }

    [UnitFact]
    public async Task GivenSecretChangesConcurrently_WhenReplacingCodes_ThenRequestIsRejected()
    {
        var actor = LocalActor();
        var store = new StubStore(Credentials(actor.SubjectId, Secret), regenerated: false);

        var result = await Handler(actor, store, new StubGenerator([
            new GeneratedRecoveryCode("AAAA-1111", [1, 2, 3])
        ])).HandleAsync(new()
        {
            Secret = Secret
        });

        Assert.False(result.Success);
        Assert.Contains(LocalRecoveryCodeRegenerationMessages.InvalidSecret, result.Errors);
    }

    [UnitFact]
    public async Task GivenLocalAuthenticationDisabled_WhenRegenerating_ThenEndpointIsHidden()
    {
        var store = new StubStore(null, regenerated: true);

        var result = await Handler(LocalActor(), store, new StubGenerator([]), enabled: false)
            .HandleAsync(new() { Secret = Secret });

        Assert.False(result.Success);
        Assert.Contains(LocalAccountMessages.Disabled, result.Errors);
        Assert.Equal(0, store.FindCount);
    }

    private static RegenerateLocalAccountRecoveryCodesCommandHandler Handler(
        RequestActor actor,
        StubStore store,
        ILocalRecoveryCodeGenerator generator,
        bool enabled = true) => new(
            store,
            generator,
            new StubActorAccessor(actor),
            new LocalAccountOptions(enabled, 2, "BRL", "pt-BR"),
            new FixedTimeProvider(Now));

    private static RequestActor LocalActor() => new(
        Guid.NewGuid(),
        3,
        null,
        [])
    {
        DisplayName = "Local User",
        IsLocal = true
    };

    private static LocalAccountCredentialSnapshot Credentials(Guid userId, string secret)
    {
        var hash = Hash.EncodeWithRandomSalt(secret, out var salt);
        return new LocalAccountCredentialSnapshot(userId, "Local User", hash, salt);
    }

    private sealed class StubActorAccessor(RequestActor actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class StubStore(
        LocalAccountCredentialSnapshot? credentials,
        bool regenerated) : ILocalAccountStore
    {
        public int FindCount { get; private set; }
        public LocalAccountRecoveryCodeRegeneration? Regeneration { get; private set; }

        public Task<LocalAccountCredentialSnapshot?> FindForAuthenticationByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            FindCount++;
            return Task.FromResult(credentials);
        }

        public Task<bool> RegenerateRecoveryCodesAsync(
            LocalAccountRecoveryCodeRegeneration regeneration,
            CancellationToken cancellationToken)
        {
            Regeneration = regeneration;
            return Task.FromResult(regenerated);
        }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocalAccountCredentialSnapshot?> FindForAuthenticationAsync(
            string name,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocalAccountCreationResult> CreateAsync(
            LocalAccountCreation creation,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocalAccountRecoveryResult> RecoverAsync(
            LocalAccountRecovery recovery,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubGenerator(IReadOnlyCollection<GeneratedRecoveryCode> codes)
        : ILocalRecoveryCodeGenerator
    {
        public int CallCount { get; private set; }

        public IReadOnlyCollection<GeneratedRecoveryCode> Generate(int count)
        {
            CallCount++;
            return codes;
        }
    }

    private sealed class ThrowingGenerator : ILocalRecoveryCodeGenerator
    {
        public IReadOnlyCollection<GeneratedRecoveryCode> Generate(int count) =>
            throw new InvalidOperationException("Simulated partial generation failure.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
