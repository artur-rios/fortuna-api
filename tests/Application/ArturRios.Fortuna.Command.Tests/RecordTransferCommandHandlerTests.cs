using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Command.Tests;

public sealed class RecordTransferCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedAccounts_WhenRecorded_ThenBothLegsAreReturned()
    {
        var profile = Profile();
        var command = AccountCommand();
        var snapshot = Snapshot(command);
        var transfers = new StubTransferStore(new(
            snapshot,
            TransferRecordOutcome.Succeeded));

        var result = await Handler(profile, transfers).HandleAsync(command);

        Assert.True(result.Success);
        Assert.Equal(snapshot.Id, result.Data?.Id);
        Assert.Equal(snapshot.OutboundTransactionId, result.Data?.OutboundTransactionId);
        Assert.Equal(snapshot.InboundTransactionId, result.Data?.InboundTransactionId);
        Assert.Equal(profile.Id, transfers.Record?.UserId);
        Assert.Equal(Now, transfers.Record?.CreatedAt);
        Assert.Contains(TransferMessages.RecordedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenStatementDestination_WhenRecorded_ThenUc23SettlementIsReturned()
    {
        var profile = Profile();
        var command = AccountCommand();
        command.DestinationFinancialAccountId = null;
        command.DestinationStatementId = Guid.NewGuid();
        var settlement = Settlement(command);
        var settlements = new StubSettlementStore(new(
            settlement,
            CreditCardStatementSettlementOutcome.Succeeded));

        var result = await Handler(
            profile,
            new StubTransferStore(new(null, TransferRecordOutcome.AccountsMustDiffer)),
            settlements).HandleAsync(command);

        Assert.True(result.Success);
        Assert.Equal(settlement.TransferId, result.Data?.Id);
        Assert.Equal(settlement.StatementId, result.Data?.DestinationStatementId);
        Assert.Equal(command.Amount, result.Data?.OutboundAmount);
        Assert.Equal(profile.Id, settlements.Request?.UserId);
    }

    [UnitTheory]
    [InlineData(TransferRecordOutcome.OriginFinancialAccountNotFound,
        TransferMessages.OriginFinancialAccountNotFound)]
    [InlineData(TransferRecordOutcome.DestinationFinancialAccountNotFound,
        TransferMessages.DestinationFinancialAccountNotFound)]
    [InlineData(TransferRecordOutcome.AccountsMustDiffer,
        TransferMessages.AccountsMustDiffer)]
    [InlineData(TransferRecordOutcome.ExchangeRateUnavailable,
        TransferMessages.ExchangeRateUnavailable)]
    [InlineData(TransferRecordOutcome.ConvertedAmountTooSmall,
        TransferMessages.ConvertedAmountTooSmall)]
    public async Task GivenAccountStoreRefusal_WhenHandled_ThenCanonicalErrorIsReturned(
        TransferRecordOutcome outcome,
        string expected)
    {
        var result = await Handler(
            Profile(),
            new StubTransferStore(new(null, outcome))).HandleAsync(AccountCommand());

        Assert.Contains(expected, result.Errors);
    }

    [UnitTheory]
    [InlineData(CreditCardStatementSettlementOutcome.StatementNotFound,
        TransferMessages.DestinationStatementNotFound)]
    [InlineData(CreditCardStatementSettlementOutcome.FinancialAccountNotFound,
        TransferMessages.OriginFinancialAccountNotFound)]
    [InlineData(CreditCardStatementSettlementOutcome.StatementOpen,
        TransferMessages.StatementOpen)]
    [InlineData(CreditCardStatementSettlementOutcome.StatementAlreadySettled,
        TransferMessages.StatementAlreadySettled)]
    [InlineData(CreditCardStatementSettlementOutcome.ExchangeRateUnavailable,
        TransferMessages.ExchangeRateUnavailable)]
    public async Task GivenSettlementRefusal_WhenHandled_ThenCanonicalErrorIsReturned(
        CreditCardStatementSettlementOutcome outcome,
        string expected)
    {
        var command = AccountCommand();
        command.DestinationFinancialAccountId = null;
        command.DestinationStatementId = Guid.NewGuid();

        var result = await Handler(
            Profile(),
            new StubTransferStore(new(null, TransferRecordOutcome.AccountsMustDiffer)),
            new StubSettlementStore(new(null, outcome))).HandleAsync(command);

        Assert.Contains(expected, result.Errors);
    }

    [UnitFact]
    public async Task GivenMissingProfile_WhenHandled_ThenNeitherStoreIsCalled()
    {
        var transfers = new StubTransferStore(new(
            Snapshot(AccountCommand()),
            TransferRecordOutcome.Succeeded));
        var settlements = new StubSettlementStore(new(
            Settlement(AccountCommand()),
            CreditCardStatementSettlementOutcome.Succeeded));

        var result = await Handler(null, transfers, settlements).HandleAsync(AccountCommand());

        Assert.Contains(TransferMessages.ProfileNotFound, result.Errors);
        Assert.Null(transfers.Record);
        Assert.Null(settlements.Request);
    }

    private static RecordTransferCommandHandler Handler(
        UserProfileSnapshot? profile,
        ITransferStore transfers,
        ICreditCardStatementSettlementStore? settlements = null) => new(
        new RecordTransferCommandValidator(new FixedTimeProvider(Now)),
        new StubActor(new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
        new StubProfileReader(profile),
        transfers,
        settlements ?? new StubSettlementStore(new(
            null,
            CreditCardStatementSettlementOutcome.StatementNotFound)),
        new FixedTimeProvider(Now));

    private static RecordTransferCommand AccountCommand() => new()
    {
        OriginFinancialAccountId = Guid.NewGuid(),
        DestinationFinancialAccountId = Guid.NewGuid(),
        Amount = 25m,
        OccurredOn = new DateOnly(2026, 9, 5)
    };

    private static TransferSnapshot Snapshot(RecordTransferCommand command) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        command.OriginFinancialAccountId,
        command.DestinationFinancialAccountId ?? Guid.NewGuid(),
        command.Amount,
        "USD",
        125m,
        "BRL",
        5m,
        command.OccurredOn,
        command.OccurredOn,
        Now);

    private static CreditCardStatementSettlementSnapshot Settlement(
        RecordTransferCommand command) => new(
        command.DestinationStatementId ?? Guid.NewGuid(),
        "Settled",
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        command.OriginFinancialAccountId,
        command.Amount,
        "USD",
        125m,
        "BRL",
        125m,
        0m,
        null,
        0m,
        5m,
        command.OccurredOn,
        command.OccurredOn);

    private static UserProfileSnapshot Profile() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Owner", "BRL", false, Now, Now);

    private sealed class StubTransferStore(TransferRecordResult result) : ITransferStore
    {
        public TransferRecord? Record { get; private set; }

        public Task<TransferRecordResult> RecordAsync(
            TransferRecord record,
            CancellationToken cancellationToken)
        {
            Record = record;
            return Task.FromResult(result);
        }
    }

    private sealed class StubSettlementStore(CreditCardStatementSettlementResult result)
        : ICreditCardStatementSettlementStore
    {
        public CreditCardStatementSettlement? Request { get; private set; }

        public Task<CreditCardStatementSettlementResult> SettleAsync(
            CreditCardStatementSettlement settlement,
            CancellationToken cancellationToken)
        {
            Request = settlement;
            return Task.FromResult(result);
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken) => Task.FromResult(profile);
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
