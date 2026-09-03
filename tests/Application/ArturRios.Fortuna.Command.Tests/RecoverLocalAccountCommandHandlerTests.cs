using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Hashing;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecoverLocalAccountCommandHandlerTests
{
    private const string RecoveryCode = "ABCD-1234";
    private const string NewSecret = "new-correct-horse-battery-staple";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-03T12:00:00Z");

    [UnitFact]
    public async Task GivenUnusedRecoveryCode_WhenRecovering_ThenSecretIsRotatedAndTokenIsIssued()
    {
        var userId = Guid.NewGuid();
        var store = new StubStore(new LocalAccountRecoveryResult(
            LocalAccountRecoveryStatus.Recovered,
            new LocalAccountRecoverySnapshot(userId, "Local User", 9)));
        var issuer = new StubTokenIssuer();

        var result = await Handler(store, issuer).HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.Equal("local-token", result.Data!.Token);
        Assert.Equal(9, result.Data.RemainingRecoveryCodes);
        Assert.Equal(userId, issuer.Subject);
        Assert.Equal("Local User", issuer.DisplayName);
        Assert.Equal(1, issuer.IssueCount);
        Assert.NotNull(store.Recovery);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(RecoveryCode)),
            store.Recovery.RecoveryCodeHash);
        Assert.True(Hash.TextMatches(NewSecret, store.Recovery.NewSecretHash, store.Recovery.NewSalt));
        Assert.Equal(Now, store.Recovery.RecoveredAt);
    }

    [UnitTheory]
    [InlineData(LocalAccountRecoveryStatus.InvalidCode, LocalAccountRecoveryMessages.InvalidRecoveryCode)]
    [InlineData(LocalAccountRecoveryStatus.Exhausted, LocalAccountRecoveryMessages.RecoveryCodesExhausted)]
    public async Task GivenRecoveryCannotProceed_WhenRecovering_ThenExpectedErrorIsReturned(
        LocalAccountRecoveryStatus status,
        string expectedError)
    {
        var store = new StubStore(new LocalAccountRecoveryResult(status, null));
        var issuer = new StubTokenIssuer();

        var result = await Handler(store, issuer).HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Errors);
        Assert.Equal(0, issuer.IssueCount);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("short")]
    public async Task GivenInvalidNewSecret_WhenRecovering_ThenCodeIsNotSubmittedForConsumption(string newSecret)
    {
        var store = new StubStore(new LocalAccountRecoveryResult(LocalAccountRecoveryStatus.InvalidCode, null));
        var command = ValidCommand();
        command.NewSecret = newSecret;

        var result = await Handler(store, new StubTokenIssuer()).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("NewSecret", StringComparison.Ordinal));
        Assert.Null(store.Recovery);
    }

    [UnitFact]
    public async Task GivenLocalAuthenticationDisabled_WhenRecovering_ThenStoreIsNotCalled()
    {
        var store = new StubStore(new LocalAccountRecoveryResult(LocalAccountRecoveryStatus.InvalidCode, null));

        var result = await Handler(store, new StubTokenIssuer(), enabled: false)
            .HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Contains(LocalAccountMessages.Disabled, result.Errors);
        Assert.Null(store.Recovery);
    }

    private static RecoverLocalAccountCommandHandler Handler(
        StubStore store,
        StubTokenIssuer issuer,
        bool enabled = true) => new(
            new RecoverLocalAccountCommandValidator(),
            store,
            issuer,
            new LocalAccountOptions(enabled, 10, "BRL", "pt-BR"),
            new FixedTimeProvider(Now));

    private static RecoverLocalAccountCommand ValidCommand() => new()
    {
        Name = "Local User",
        RecoveryCode = RecoveryCode,
        NewSecret = NewSecret
    };

    private sealed class StubStore(LocalAccountRecoveryResult result) : ILocalAccountStore
    {
        public LocalAccountRecovery? Recovery { get; private set; }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocalAccountCredentialSnapshot?> FindForAuthenticationAsync(
            string name,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocalAccountCreationResult> CreateAsync(
            LocalAccountCreation creation,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocalAccountRecoveryResult> RecoverAsync(
            LocalAccountRecovery recovery,
            CancellationToken cancellationToken)
        {
            Recovery = recovery;
            return Task.FromResult(result);
        }
    }

    private sealed class StubTokenIssuer : ILocalAuthTokenIssuer
    {
        public Guid Subject { get; private set; }
        public string? DisplayName { get; private set; }
        public int IssueCount { get; private set; }

        public LocalAuthToken Issue(Guid subject, string displayName)
        {
            Subject = subject;
            DisplayName = displayName;
            IssueCount++;
            return new LocalAuthToken("local-token", Now.AddHours(1));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
