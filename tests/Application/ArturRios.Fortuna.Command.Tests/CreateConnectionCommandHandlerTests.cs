using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class CreateConnectionCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 7, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidPluggyItem_WhenCreated_ThenTokenIsProtectedAndConnectionReturned()
    {
        var profile = Profile();
        var store = new StubConnectionStore();
        var protector = new StubProtector();
        var handler = Handler(
            profile,
            store,
            new StubGateway(new(
                PluggyConnectionValidationOutcome.Succeeded,
                "Nubank",
                "plain-access-token")),
            protector);

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.Equal("Nubank", result.Data?.Institution);
        Assert.Equal(ConnectionStatus.Active, result.Data?.Status);
        Assert.Equal("plain-access-token", protector.ProtectedValue);
        Assert.Equal(new byte[] { 7, 8, 9 }, store.Creation?.AccessTokenCipher);
        Assert.Equal(Now, store.Creation?.CreatedAt);
        Assert.Contains(ConnectionMessages.CreatedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenExistingConnection_WhenCreated_ThenConflictReturnsExistingWithoutPluggyCall()
    {
        var existing = Snapshot();
        var store = new StubConnectionStore
        {
            Result = new(existing, ConnectionMutationOutcome.Duplicate)
        };
        var gateway = new StubGateway(new(
            PluggyConnectionValidationOutcome.Succeeded,
            "Nubank",
            "token"));
        var handler = Handler(Profile(), store, gateway, new StubProtector());

        var result = await handler.HandleAsync(ValidCommand(existing.ExternalReference));

        Assert.False(result.Success);
        Assert.Equal(existing.Id, result.Data?.Id);
        Assert.Equal(1, gateway.CallCount);
        Assert.Contains(ConnectionMessages.Duplicate, result.Errors);
    }

    [UnitTheory]
    [InlineData(PluggyConnectionValidationOutcome.InvalidReference, ConnectionMessages.InvalidReference)]
    [InlineData(PluggyConnectionValidationOutcome.Unavailable, ConnectionMessages.SourceUnavailable)]
    [InlineData(PluggyConnectionValidationOutcome.NotConfigured, ConnectionMessages.SourceNotAvailable)]
    public async Task GivenPluggyFailure_WhenCreated_ThenExpectedErrorIsReturned(
        PluggyConnectionValidationOutcome outcome,
        string expectedError)
    {
        var store = new StubConnectionStore();
        var handler = Handler(Profile(), store, new StubGateway(new(outcome)), new StubProtector());

        var result = await handler.HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Null(store.Creation);
        Assert.Contains(expectedError, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenCreated_ThenNoExternalCallIsMade()
    {
        var gateway = new StubGateway(new(PluggyConnectionValidationOutcome.Unavailable));
        var handler = Handler(null, new StubConnectionStore(), gateway, new StubProtector());

        var result = await handler.HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Equal(0, gateway.CallCount);
        Assert.Contains(ConnectionMessages.ProfileNotFound, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingCurrentConsent_WhenCreated_ThenPluggyIsNotCalled()
    {
        var gateway = new StubGateway(new(PluggyConnectionValidationOutcome.Unavailable));
        var handler = Handler(
            Profile(),
            new StubConnectionStore(),
            gateway,
            new StubProtector(),
            consentCurrent: false);

        var result = await handler.HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.Equal(0, gateway.CallCount);
        Assert.Contains(ProcessingConsentMessages.ExternalDataProcessingRequired, result.Errors);
    }

    [UnitFact]
    public async Task GivenConcurrentDuplicate_WhenStored_ThenConflictReturnsWinningConnection()
    {
        var store = new StubConnectionStore
        {
            Result = new(Snapshot(), ConnectionMutationOutcome.Duplicate)
        };
        var handler = Handler(
            Profile(),
            store,
            new StubGateway(new(
                PluggyConnectionValidationOutcome.Succeeded,
                "Nubank",
                "token")),
            new StubProtector());

        var result = await handler.HandleAsync(ValidCommand());

        Assert.False(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains(ConnectionMessages.Duplicate, result.Errors);
    }

    private static CreateConnectionCommandHandler Handler(
        UserProfileSnapshot? profile,
        StubConnectionStore store,
        StubGateway gateway,
        StubProtector protector,
        bool consentCurrent = true) => new(
            new CreateConnectionCommandValidator(),
            new StubActorAccessor(new RequestActor(
                profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
            new StubProfileReader(profile),
            store,
            gateway,
            protector,
            new FixedTimeProvider(Now),
            new StubConsentReader(consentCurrent),
            new ProcessingConsentOptions("1.0"));

    private static CreateConnectionCommand ValidCommand(string? reference = null) => new()
    {
        DataSource = "pluggy",
        ExternalReference = reference ?? Guid.NewGuid().ToString()
    };

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private static ConnectionSnapshot Snapshot() => new(
        Guid.NewGuid(), TransactionSourceType.Pluggy, Guid.NewGuid().ToString(),
        ConnectionStatus.Active, Now, Now);

    private sealed class StubConnectionStore : IConnectionStore
    {
        public ConnectionCreation? Creation { get; private set; }
        public ConnectionMutationResult Result { get; init; } = new(
            Snapshot(), ConnectionMutationOutcome.Succeeded);

        public Task<ConnectionMutationResult> CreateAsync(
            ConnectionCreation creation, CancellationToken cancellationToken)
        {
            Creation = creation;
            return Task.FromResult(Result);
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
        public string? ProtectedValue { get; private set; }

        public byte[] Protect(string accessToken)
        {
            ProtectedValue = accessToken;
            return [7, 8, 9];
        }

        public string Unprotect(byte[] protectedAccessToken) => "plain-access-token";
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject, CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId, CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubConsentReader(bool isCurrent) : IProcessingConsentReader
    {
        public Task<IReadOnlyCollection<ProcessingConsentSnapshot>> ListAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ProcessingConsentSnapshot>>([]);

        public Task<bool> IsCurrentAsync(
            Guid userId,
            ProcessingConsentPurpose purpose,
            string version,
            CancellationToken cancellationToken) => Task.FromResult(isCurrent);
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
