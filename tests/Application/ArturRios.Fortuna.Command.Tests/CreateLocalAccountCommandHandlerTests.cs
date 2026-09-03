using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Hashing;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CreateLocalAccountCommandHandlerTests
{
    private const string Secret = "correct-horse-battery-staple";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");

    [UnitFact]
    public async Task GivenValidInput_WhenLocalAccountIsCreated_ThenHashesAndOneTimeCodesAreReturned()
    {
        var store = new FakeLocalAccountStore();
        var handler = Handler(store: store, recoveryCodeCount: 4);

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(4, result.Data.RecoveryCodes.Count);
        Assert.Equal(4, result.Data.RecoveryCodes.Distinct().Count());
        Assert.Equal(LocalAccountMessages.RecoveryWarning, result.Data.RecoveryWarning);
        Assert.NotNull(store.Creation);
        Assert.True(Hash.TextMatches(Secret, store.Creation.SecretHash, store.Creation.Salt));
        Assert.Equal(
            result.Data.RecoveryCodes.Select(HashRecoveryCode),
            store.Creation.RecoveryCodeHashes,
            ByteArrayComparer.Instance);
        Assert.Equal(Now, store.Creation.CreatedAt);
    }

    [UnitFact]
    public async Task GivenLocalAuthenticationDisabled_WhenCreatingAccount_ThenEndpointErrorIsReturned()
    {
        var store = new FakeLocalAccountStore();

        var result = await Handler(store: store, enabled: false).HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(LocalAccountMessages.Disabled, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenAccountAlreadyExists_WhenCreatingAccount_ThenConflictErrorIsReturned()
    {
        var store = new FakeLocalAccountStore { Exists = true };

        var result = await Handler(store: store).HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(LocalAccountMessages.AlreadyExists, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenConcurrentCreationWins_WhenAccountIsPersisted_ThenConflictErrorIsReturned()
    {
        var store = new FakeLocalAccountStore { RaceLost = true };

        var result = await Handler(store: store).HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(LocalAccountMessages.AlreadyExists, result.Errors);
    }

    [UnitTheory]
    [InlineData("", Secret, LocalAccountMessages.NameRequired)]
    [InlineData("Local User", "short", LocalAccountMessages.SecretTooShort)]
    public async Task GivenInvalidInput_WhenCreatingAccount_ThenFieldErrorIsReturned(
        string displayName,
        string secret,
        string expectedError)
    {
        var store = new FakeLocalAccountStore();
        var command = ValidCommand();
        command.DisplayName = displayName;
        command.Secret = secret;

        var result = await Handler(store: store).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenInvalidStorageMode_WhenCreatingAccount_ThenFieldErrorIsReturned()
    {
        var store = new FakeLocalAccountStore();
        var command = ValidCommand();
        command.StorageMode = (LocalAccountStorageMode)99;

        var result = await Handler(store: store).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(LocalAccountMessages.StorageModeInvalid, result.Errors);
        Assert.Null(store.Creation);
    }

    [UnitFact]
    public async Task GivenCredentialStoreUnavailable_WhenCreatingAccount_ThenInMemoryModeIsOffered()
    {
        var store = new FakeLocalAccountStore();
        var command = ValidCommand();
        command.StorageMode = LocalAccountStorageMode.OperatingSystem;

        var result = await Handler(store: store, credentialStoreAvailable: false).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(LocalAccountMessages.CredentialStoreUnavailable, result.Errors);
        Assert.Contains("InMemory", result.Errors.Single(), StringComparison.Ordinal);
        Assert.Null(store.Creation);
    }

    private static CreateLocalAccountCommandHandler Handler(
        FakeLocalAccountStore store,
        bool enabled = true,
        bool credentialStoreAvailable = true,
        int recoveryCodeCount = 10) => new(
            new CreateLocalAccountCommandValidator(),
            store,
            new FakeCredentialStoreAvailability(credentialStoreAvailable),
            new LocalAccountOptions(enabled, recoveryCodeCount, "BRL", "pt-BR"),
            new FixedTimeProvider(Now));

    private static CreateLocalAccountCommand ValidCommand() => new()
    {
        DisplayName = "Local User",
        Secret = Secret,
        StorageMode = LocalAccountStorageMode.InMemory
    };

    private static byte[] HashRecoveryCode(string recoveryCode) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(recoveryCode));

    private sealed class FakeLocalAccountStore : ILocalAccountStore
    {
        public bool Exists { get; init; }
        public bool RaceLost { get; init; }
        public LocalAccountCreation? Creation { get; private set; }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken) => Task.FromResult(Exists);

        public Task<LocalAccountCredentialSnapshot?> FindForAuthenticationAsync(
            string name,
            CancellationToken cancellationToken) => Task.FromResult<LocalAccountCredentialSnapshot?>(null);

        public Task<LocalAccountCreationResult> CreateAsync(
            LocalAccountCreation creation,
            CancellationToken cancellationToken)
        {
            Creation = creation;
            if (RaceLost)
            {
                return Task.FromResult(new LocalAccountCreationResult(null, true));
            }

            return Task.FromResult(new LocalAccountCreationResult(
                new LocalAccountSnapshot(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    creation.DisplayName,
                    creation.StorageMode,
                    creation.CreatedAt),
                false));
        }
    }

    private sealed class FakeCredentialStoreAvailability(bool available)
        : ILocalCredentialStoreAvailability
    {
        public bool IsAvailable(LocalAccountStorageMode storageMode) => available;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y) =>
            ReferenceEquals(x, y) || x is not null && y is not null && x.SequenceEqual(y);

        public int GetHashCode(byte[] value) => value.Aggregate(17, (hash, item) => hash * 31 + item);
    }
}
