using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class ReconcileTransactionCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValuesWithinTolerance_WhenReconciled_ThenMatchHasNoDiscrepancy()
    {
        var profile = Profile();
        var command = ValidCommand();
        var transaction = Snapshot(command, importedAmount: 10.01m, importedDateOffset: 1);
        var store = new StubStore(new TransactionReconciliationResult(
            transaction,
            TransactionReconciliationOutcome.Succeeded));

        var result = await Handler(profile, store).HandleAsync(command);

        Assert.True(result.Success);
        Assert.True(result.Data?.IsReconciled);
        Assert.False(result.Data?.Reconciliation?.HasDiscrepancy);
        Assert.Equal(transaction.ImportedAmount, result.Data?.Reconciliation?.ImportedAmount);
        Assert.Equal(profile.Id, store.Change?.UserId);
        Assert.Equal(Now, store.Change?.ChangedAt);
        Assert.Contains(TransactionMessages.ReconciledSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenValuesBeyondTolerance_WhenReconciled_ThenBothFiguresAreFlagged()
    {
        var command = ValidCommand();
        var transaction = Snapshot(command, importedAmount: 12m, importedDateOffset: 3);
        var store = new StubStore(new TransactionReconciliationResult(
            transaction,
            TransactionReconciliationOutcome.Succeeded));

        var result = await Handler(Profile(), store).HandleAsync(command);

        Assert.True(result.Data?.Reconciliation?.HasDiscrepancy);
        Assert.Equal(10m, result.Data?.Reconciliation?.TransactionAmount);
        Assert.Equal(12m, result.Data?.Reconciliation?.ImportedAmount);
        Assert.Equal(transaction.OccurredOn, result.Data?.Reconciliation?.TransactionOccurredOn);
        Assert.Equal(transaction.ImportedOccurredOn,
            result.Data?.Reconciliation?.ImportedOccurredOn);
    }

    [UnitFact]
    public async Task GivenReconciledTransaction_WhenUnreconciled_ThenLinkIsAbsent()
    {
        var command = new ReconcileTransactionCommand
        {
            Id = Guid.NewGuid(),
            Unreconcile = true
        };
        var transaction = Snapshot(command, isReconciled: false);
        var store = new StubStore(new TransactionReconciliationResult(
            transaction,
            TransactionReconciliationOutcome.Succeeded));

        var result = await Handler(Profile(), store).HandleAsync(command);

        Assert.True(result.Success);
        Assert.False(result.Data?.IsReconciled);
        Assert.Null(result.Data?.Reconciliation);
        Assert.True(store.Change?.Unreconcile);
        Assert.Contains(TransactionMessages.UnreconciledSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(TransactionReconciliationOutcome.TransactionNotFound,
        TransactionMessages.NotFound)]
    [InlineData(TransactionReconciliationOutcome.ImportedRecordNotFound,
        TransactionMessages.ImportedRecordNotFound)]
    [InlineData(TransactionReconciliationOutcome.TransactionAlreadyReconciled,
        TransactionMessages.AlreadyReconciled)]
    [InlineData(TransactionReconciliationOutcome.TransactionNotReconciled,
        TransactionMessages.NotReconciled)]
    [InlineData(TransactionReconciliationOutcome.SettledStatementFrozen,
        TransactionMessages.SettledStatementFrozen)]
    public async Task GivenStoreRefusal_WhenChanged_ThenCanonicalErrorIsReturned(
        TransactionReconciliationOutcome outcome,
        string expected)
    {
        var store = new StubStore(new TransactionReconciliationResult(null, outcome));

        var result = await Handler(Profile(), store).HandleAsync(ValidCommand());

        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenRecordMatchedElsewhere_WhenReconciled_ThenOtherTransactionIsNamed()
    {
        var conflictingId = Guid.NewGuid();
        var store = new StubStore(new TransactionReconciliationResult(
            null,
            TransactionReconciliationOutcome.ImportedRecordAlreadyMatched,
            conflictingId));

        var result = await Handler(Profile(), store).HandleAsync(ValidCommand());

        Assert.Contains(TransactionMessages.ImportedRecordAlreadyMatched, result.Errors);
        Assert.Contains(TransactionMessages.ConflictingTransaction(conflictingId), result.Errors);
    }

    [UnitFact]
    public async Task GivenInvalidCommand_WhenReconciled_ThenStorageIsNotCalled()
    {
        var store = new StubStore(new TransactionReconciliationResult(
            null,
            TransactionReconciliationOutcome.Succeeded));
        var command = ValidCommand();
        command.ImportedRecordId = 0;

        var result = await Handler(Profile(), store).HandleAsync(command);

        Assert.Contains(TransactionMessages.ImportedRecordIdRequired, result.Errors);
        Assert.Null(store.Change);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenReconciled_ThenStorageIsNotCalled()
    {
        var store = new StubStore(new TransactionReconciliationResult(
            null,
            TransactionReconciliationOutcome.Succeeded));

        var result = await Handler(null, store).HandleAsync(ValidCommand());

        Assert.Contains(TransactionMessages.ProfileNotFound, result.Errors);
        Assert.Null(store.Change);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenReconciled_ThenProfileUsesPublicIdentifier()
    {
        var profile = Profile();
        var profiles = new StubProfileReader(profile);
        var command = ValidCommand();
        var store = new StubStore(new TransactionReconciliationResult(
            Snapshot(command),
            TransactionReconciliationOutcome.Succeeded));
        var handler = new ReconcileTransactionCommandHandler(
            new ReconcileTransactionCommandValidator(),
            new StubActor(new RequestActor(profile.Id, 3, null, []) { IsLocal = true }),
            profiles,
            store,
            new ReconciliationOptions(0.01m, 1),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(command);

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static ReconcileTransactionCommandHandler Handler(
        UserProfileSnapshot? profile,
        ITransactionReconciliationStore store) => new(
        new ReconcileTransactionCommandValidator(),
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfileReader(profile),
        store,
        new ReconciliationOptions(0.01m, 1),
        new FixedTimeProvider(Now));

    private static ReconcileTransactionCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        ImportJobId = Guid.NewGuid(),
        ImportedRecordId = 42
    };

    private static TransactionReadSnapshot Snapshot(
        ReconcileTransactionCommand command,
        decimal importedAmount = 10m,
        int importedDateOffset = 0,
        bool isReconciled = true) => new()
        {
            Id = command.Id,
            UserId = Guid.NewGuid(),
            FinancialAccountId = Guid.NewGuid(),
            FinancialAccountName = "Checking",
            CategoryId = Guid.NewGuid(),
            CategoryName = "General",
            Direction = TransactionDirection.Expense,
            Amount = 10m,
            CurrencyCode = "BRL",
            OccurredOn = new DateOnly(2026, 9, 4),
            SourceType = TransactionSourceType.Manual,
            IsReconciled = isReconciled,
            ImportJobId = isReconciled ? command.ImportJobId : null,
            ImportedRecordId = isReconciled ? command.ImportedRecordId : null,
            ImportedAmount = isReconciled ? importedAmount : null,
            ImportedOccurredOn = isReconciled
            ? new DateOnly(2026, 9, 4).AddDays(importedDateOffset)
            : null,
            CreatedAt = Now.AddHours(-1),
            UpdatedAt = Now
        };

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Owner",
        "BRL",
        false,
        Now,
        Now);

    private sealed class StubStore(TransactionReconciliationResult result)
        : ITransactionReconciliationStore
    {
        public TransactionReconciliation? Change { get; private set; }

        public Task<TransactionReconciliationResult> ReconcileAsync(
            TransactionReconciliation change,
            CancellationToken cancellationToken)
        {
            Change = change;
            return Task.FromResult(result);
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public bool PublicIdLookupUsed { get; private set; }

        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken)
        {
            PublicIdLookupUsed = true;
            return Task.FromResult(profile);
        }
    }

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
