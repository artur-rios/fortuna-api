using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Hashing;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class AuthenticateLocalAccountCommandHandlerTests
{
    private const string Secret = "correct-horse-battery-staple";

    [UnitFact]
    public async Task GivenMatchingLocalCredentials_WhenAuthenticating_ThenTokenIsIssued()
    {
        var credentials = Credentials(Secret);
        var issuer = new StubTokenIssuer();
        var handler = Handler(new StubStore(credentials), issuer);

        var result = await handler.HandleAsync(new AuthenticateLocalAccountCommand
        {
            Name = "Local User",
            Secret = Secret
        });

        Assert.True(result.Success);
        Assert.Equal("local-token", result.Data!.Token);
        Assert.Equal(credentials.UserId, issuer.Subject);
        Assert.Equal(credentials.DisplayName, issuer.DisplayName);
        Assert.Equal(1, issuer.IssueCount);
    }

    [UnitFact]
    public async Task GivenWrongSecretOrUnknownName_WhenAuthenticating_ThenResponsesAreIdentical()
    {
        var credentials = Credentials(Secret);
        var wrongSecretIssuer = new StubTokenIssuer();
        var unknownNameIssuer = new StubTokenIssuer();
        var handler = Handler(new StubStore(credentials), wrongSecretIssuer);

        var wrongSecret = await handler.HandleAsync(new AuthenticateLocalAccountCommand
        {
            Name = "Local User",
            Secret = "wrong-secret"
        });
        var unknownName = await Handler(new StubStore(null), unknownNameIssuer)
            .HandleAsync(new AuthenticateLocalAccountCommand
            {
                Name = "Unknown User",
                Secret = Secret
            });

        Assert.False(wrongSecret.Success);
        Assert.False(unknownName.Success);
        Assert.Equal(wrongSecret.Errors.ToArray(), unknownName.Errors.ToArray());
        Assert.Contains(LocalAuthenticationMessages.InvalidCredentials, wrongSecret.Errors);
        Assert.Equal(0, wrongSecretIssuer.IssueCount);
        Assert.Equal(0, unknownNameIssuer.IssueCount);
    }

    [UnitFact]
    public async Task GivenLocalAuthenticationDisabled_WhenAuthenticating_ThenEndpointErrorIsReturned()
    {
        var issuer = new StubTokenIssuer();
        var result = await Handler(new StubStore(null), issuer, enabled: false)
            .HandleAsync(new AuthenticateLocalAccountCommand());

        Assert.False(result.Success);
        Assert.Contains(LocalAccountMessages.Disabled, result.Errors);
        Assert.Equal(0, issuer.IssueCount);
    }

    private static AuthenticateLocalAccountCommandHandler Handler(
        StubStore store,
        StubTokenIssuer issuer,
        bool enabled = true) => new(
            store,
            issuer,
            new LocalAccountOptions(enabled, 10, "BRL", "pt-BR"));

    private static LocalAccountCredentialSnapshot Credentials(string secret)
    {
        var hash = Hash.EncodeWithRandomSalt(secret, out var salt);
        return new LocalAccountCredentialSnapshot(Guid.NewGuid(), "Local User", hash, salt);
    }

    private sealed class StubStore(LocalAccountCredentialSnapshot? credentials) : ILocalAccountStore
    {
        public Task<bool> ExistsAsync(CancellationToken cancellationToken) => Task.FromResult(credentials is not null);

        public Task<LocalAccountCredentialSnapshot?> FindForAuthenticationAsync(
            string name,
            CancellationToken cancellationToken) => Task.FromResult(credentials);

        public Task<LocalAccountCreationResult> CreateAsync(
            LocalAccountCreation creation,
            CancellationToken cancellationToken) => throw new NotSupportedException();
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
            return new LocalAuthToken("local-token", DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
