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

public sealed class UpdateTransactionCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenValidChanges_WhenUpdated_ThenMappedTransactionIsReturned()
    {
        var profile = Profile();
        var command = ValidCommand();
        var snapshot = Snapshot(command);
        var updater = new StubTransactionUpdater(new TransactionUpdateResult(
            snapshot,
            TransactionUpdateOutcome.Succeeded));

        var result = await Handler(profile, updater).HandleAsync(command);

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(snapshot.Amount, result.Data?.Amount);
        Assert.Equal(snapshot.CategoryName, result.Data?.CategoryName);
        Assert.Equal(snapshot.IsManuallyCorrected, result.Data?.IsManuallyCorrected);
        Assert.Single(result.Data!.Tags);
        Assert.Equal(profile.Id, updater.Update?.UserId);
        Assert.Equal(Now, updater.Update?.UpdatedAt);
        Assert.Contains(TransactionMessages.UpdatedSuccessfully, result.Messages);
    }

    [UnitTheory]
    [InlineData(TransactionUpdateOutcome.NotFound, TransactionMessages.NotFound)]
    [InlineData(TransactionUpdateOutcome.CategoryNotFound, TransactionMessages.CategoryNotFound)]
    [InlineData(TransactionUpdateOutcome.SettledStatementFrozen,
        TransactionMessages.SettledStatementFrozen)]
    [InlineData(TransactionUpdateOutcome.TransferFieldsRestricted,
        TransactionMessages.TransferFieldsRestricted)]
    [InlineData(TransactionUpdateOutcome.InstallmentFieldsRestricted,
        TransactionMessages.InstallmentFieldsRestricted)]
    public async Task GivenStoreRefusal_WhenUpdated_ThenCanonicalErrorIsReturned(
        TransactionUpdateOutcome outcome,
        string expected)
    {
        var updater = new StubTransactionUpdater(new TransactionUpdateResult(null, outcome));

        var result = await Handler(Profile(), updater).HandleAsync(ValidCommand());

        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenInvalidCommand_WhenUpdated_ThenStorageIsNotCalled()
    {
        var updater = new StubTransactionUpdater(new TransactionUpdateResult(
            Snapshot(ValidCommand()),
            TransactionUpdateOutcome.Succeeded));
        var command = ValidCommand();
        command.Amount = 0m;

        var result = await Handler(Profile(), updater).HandleAsync(command);

        Assert.Contains(TransactionMessages.AmountPositive, result.Errors);
        Assert.Null(updater.Update);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenUpdated_ThenStorageIsNotCalled()
    {
        var updater = new StubTransactionUpdater(new TransactionUpdateResult(
            Snapshot(ValidCommand()),
            TransactionUpdateOutcome.Succeeded));

        var result = await Handler(null, updater).HandleAsync(ValidCommand());

        Assert.Contains(TransactionMessages.ProfileNotFound, result.Errors);
        Assert.Null(updater.Update);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenUpdated_ThenProfileIsResolvedByPublicId()
    {
        var profile = Profile();
        var profiles = new StubProfileReader(profile);
        var updater = new StubTransactionUpdater(new TransactionUpdateResult(
            Snapshot(ValidCommand()),
            TransactionUpdateOutcome.Succeeded));
        var handler = new UpdateTransactionCommandHandler(
            new UpdateTransactionCommandValidator(new FixedTimeProvider(Now)),
            new StubActor(new RequestActor(profile.Id, 3, null, []) { IsLocal = true }),
            profiles,
            updater,
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(ValidCommand());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static UpdateTransactionCommandHandler Handler(
        UserProfileSnapshot? profile,
        ITransactionUpdater updater) => new(
        new UpdateTransactionCommandValidator(new FixedTimeProvider(Now)),
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfileReader(profile),
        updater,
        new FixedTimeProvider(Now));

    private static UpdateTransactionCommand ValidCommand() => new()
    {
        Id = Guid.NewGuid(),
        OccurredOn = new DateOnly(2026, 9, 5),
        Amount = 40m,
        Direction = TransactionDirection.Expense,
        CategoryId = Guid.NewGuid(),
        Description = "Updated",
        Counterparty = "Cafe",
        Tags = ["Food"]
    };

    private static TransactionReadSnapshot Snapshot(UpdateTransactionCommand command) => new()
    {
        Id = command.Id,
        UserId = Guid.NewGuid(),
        FinancialAccountId = Guid.NewGuid(),
        FinancialAccountName = "Checking",
        CategoryId = command.CategoryId,
        CategoryName = "Dining",
        CounterpartyId = Guid.NewGuid(),
        CounterpartyName = command.Counterparty,
        Direction = command.Direction,
        Amount = command.Amount,
        CurrencyCode = "BRL",
        OccurredOn = command.OccurredOn,
        Description = command.Description,
        SourceType = TransactionSourceType.Excel,
        IsManuallyCorrected = true,
        Tags = [new TransactionReadTagSnapshot(Guid.NewGuid(), "Food")],
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

    private sealed class StubTransactionUpdater(TransactionUpdateResult result)
        : ITransactionUpdater
    {
        public TransactionUpdate? Update { get; private set; }

        public Task<TransactionUpdateResult> UpdateAsync(
            TransactionUpdate update,
            CancellationToken cancellationToken)
        {
            Update = update;
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
