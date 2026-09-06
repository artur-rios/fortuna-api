using System.Text.Json;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class ReauthenticateConnectionCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenConnectionRequiringAuthorization_WhenReauthenticated_ThenItBecomesActive()
    {
        var connection = Snapshot(ConnectionStatus.RequiresReauthentication);
        var store = new StubStore(connection);
        var protector = new StubProtector();
        var gateway = new StubGateway(new(
            PluggyConnectionValidationOutcome.Succeeded, "Nubank", "new-token"));

        var result = await Handler(connection, store, gateway, protector).HandleAsync(
            Command());

        Assert.True(result.Success);
        Assert.Equal(ConnectionStatus.Active, result.Data?.Status);
        Assert.Equal("Nubank", result.Data?.Institution);
        Assert.Equal("new-token", protector.Value);
        Assert.Equal([7, 8, 9], store.Request?.AccessTokenCipher);
        Assert.Equal(Now, store.Request?.UpdatedAt);
        Assert.Contains(ConnectionMessages.ReauthenticatedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(ConnectionStatus.Active, ConnectionMessages.ReauthenticationNotRequired)]
    [InlineData(ConnectionStatus.Revoked, ConnectionMessages.Revoked)]
    public async Task GivenConnectionInWrongState_WhenReauthenticated_ThenPluggyIsNotCalled(
        ConnectionStatus status,
        string expected)
    {
        var connection = Snapshot(status);
        var gateway = new StubGateway(new(PluggyConnectionValidationOutcome.Succeeded));

        var result = await Handler(
            connection,
            new StubStore(connection),
            gateway,
            new StubProtector()).HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
        Assert.Equal(0, gateway.CallCount);
    }

    [UnitTheory]
    [InlineData(PluggyConnectionValidationOutcome.InvalidReference,
        ConnectionMessages.InvalidReference)]
    [InlineData(PluggyConnectionValidationOutcome.Unavailable,
        ConnectionMessages.SourceUnavailable)]
    [InlineData(PluggyConnectionValidationOutcome.NotConfigured,
        ConnectionMessages.SourceNotAvailable)]
    public async Task GivenPluggyRejectsReference_WhenReauthenticated_ThenConnectionIsUntouched(
        PluggyConnectionValidationOutcome outcome,
        string expected)
    {
        var connection = Snapshot(ConnectionStatus.RequiresReauthentication);
        var store = new StubStore(connection);

        var result = await Handler(
            connection,
            store,
            new StubGateway(new(outcome)),
            new StubProtector()).HandleAsync(Command());

        Assert.False(result.Success);
        Assert.Contains(expected, result.Errors);
        Assert.Null(store.Request);
    }

    [UnitFact]
    public async Task GivenBankCredentialField_WhenReauthenticated_ThenRequestIsRejected()
    {
        var connection = Snapshot(ConnectionStatus.RequiresReauthentication);
        var gateway = new StubGateway(new(PluggyConnectionValidationOutcome.Succeeded));
        var command = Command();
        command.AdditionalFields = new Dictionary<string, JsonElement>
        {
            ["password"] = JsonDocument.Parse("\"secret\"").RootElement.Clone()
        };

        var result = await Handler(
            connection,
            new StubStore(connection),
            gateway,
            new StubProtector()).HandleAsync(command);

        Assert.False(result.Success);
        Assert.Contains(ConnectionMessages.BankCredentialRejected, result.Errors);
        Assert.Equal(0, gateway.CallCount);
    }

    private static ReauthenticateConnectionCommandHandler Handler(
        ConnectionSnapshot connection,
        StubStore store,
        StubGateway gateway,
        StubProtector protector)
    {
        var profile = new UserProfileSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);
        return new ReauthenticateConnectionCommandHandler(
            new ReauthenticateConnectionCommandValidator(),
            new StubActorAccessor(new RequestActor(profile.ExternalSubject!.Value, 3, null, [])),
            new StubProfileReader(profile),
            new StubReader(profile.Id, connection),
            store,
            gateway,
            protector,
            new FixedTimeProvider(Now));
    }

    private static ReauthenticateConnectionCommand Command() => new()
    {
        Id = Guid.NewGuid(),
        ExternalReference = Guid.NewGuid().ToString()
    };

    private static ConnectionSnapshot Snapshot(ConnectionStatus status) => new(
        Guid.NewGuid(), TransactionSourceType.Pluggy, Guid.NewGuid().ToString(),
        status, Now.AddDays(-1), Now.AddMinutes(-1));

    private sealed class StubReader(Guid userId, ConnectionSnapshot connection) : IConnectionReader
    {
        public IQueryable<Connection> Query() => throw new NotSupportedException();

        public Task<ConnectionSnapshot?> FindByIdAsync(
            Guid requestedUserId, Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ConnectionSnapshot?>(requestedUserId == userId ? connection : null);
    }

    private sealed class StubStore(ConnectionSnapshot connection)
        : IConnectionReauthenticationStore
    {
        public ConnectionReauthentication? Request { get; private set; }

        public Task<ConnectionReauthenticationResult> ReauthenticateAsync(
            ConnectionReauthentication reauthentication,
            CancellationToken cancellationToken)
        {
            Request = reauthentication;
            return Task.FromResult(new ConnectionReauthenticationResult(
                connection with
                {
                    ExternalReference = reauthentication.ExternalReference,
                    Status = ConnectionStatus.Active,
                    UpdatedAt = reauthentication.UpdatedAt
                },
                ConnectionReauthenticationOutcome.Succeeded));
        }
    }

    private sealed class StubGateway(PluggyConnectionValidation result) : IPluggyConnectionGateway
    {
        public int CallCount { get; private set; }

        public Task<PluggyConnectionValidation> ValidateAsync(
            string externalReference, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubProtector : IConnectionAccessTokenProtector
    {
        public string? Value { get; private set; }

        public byte[] Protect(string accessToken)
        {
            Value = accessToken;
            return [7, 8, 9];
        }

        public string Unprotect(byte[] protectedAccessToken) => throw new NotSupportedException();
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject, CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId, CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
