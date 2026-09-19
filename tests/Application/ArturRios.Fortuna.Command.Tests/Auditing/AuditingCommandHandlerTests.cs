using ArturRios.Fortuna.Command.Auditing;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArturRios.Fortuna.Command.Tests.Auditing;

public sealed class AuditStubCommand : BaseCommand;

public sealed class AuditStubOutput : CommandOutput
{
    public Guid Id { get; set; }
}

public sealed class AuditingCommandHandlerTests
{
    [UnitFact]
    public async Task GivenSuccessfulCommand_WhenHandled_ThenOneSuccessfulEntryIsWritten()
    {
        var entityId = Guid.NewGuid();
        var inner = new Mock<ICommandHandlerAsync<AuditStubCommand, AuditStubOutput>>();
        inner.Setup(handler => handler.HandleAsync(It.IsAny<AuditStubCommand>()))
            .ReturnsAsync(DataOutput<AuditStubOutput?>.New.WithData(new AuditStubOutput { Id = entityId }));
        var writer = new Mock<IAuditEntryWriter>();
        var handler = Handler(inner, writer);

        var result = await handler.HandleAsync(new AuditStubCommand());

        Assert.True(result.Success);
        writer.Verify(entry => entry.WriteAsync(
            nameof(AuditStubCommand),
            "AuditStub",
            entityId,
            true,
            null), Times.Once);
    }

    [UnitFact]
    public async Task GivenRefusedCommand_WhenHandled_ThenCanonicalReasonIsWritten()
    {
        var inner = new Mock<ICommandHandlerAsync<AuditStubCommand, AuditStubOutput>>();
        inner.Setup(handler => handler.HandleAsync(It.IsAny<AuditStubCommand>()))
            .ReturnsAsync(DataOutput<AuditStubOutput?>.New.WithErrors(["first", "second"]));
        var writer = new Mock<IAuditEntryWriter>();

        var result = await Handler(inner, writer).HandleAsync(new AuditStubCommand());

        Assert.False(result.Success);
        writer.Verify(entry => entry.WriteAsync(
            nameof(AuditStubCommand),
            null,
            null,
            false,
            "first"), Times.Once);
    }

    [UnitFact]
    public async Task GivenAuditStoreFailure_WhenCommandSucceeds_ThenOriginalResultIsReturned()
    {
        var inner = new Mock<ICommandHandlerAsync<AuditStubCommand, AuditStubOutput>>();
        inner.Setup(handler => handler.HandleAsync(It.IsAny<AuditStubCommand>()))
            .ReturnsAsync(DataOutput<AuditStubOutput?>.New.WithData(new AuditStubOutput()));
        var writer = new Mock<IAuditEntryWriter>();
        writer.Setup(entry => entry.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool>(),
                It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("audit unavailable"));

        var result = await Handler(inner, writer).HandleAsync(new AuditStubCommand());

        Assert.True(result.Success);
    }

    [UnitFact]
    public async Task GivenRestoreCommand_WhenAudited_ThenCanonicalEntityTypeIsWritten()
    {
        var id = Guid.NewGuid();
        var inner = new Mock<ICommandHandlerAsync<
            RestoreFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>>();
        inner.Setup(handler => handler.HandleAsync(It.IsAny<RestoreFinancialAccountCommand>()))
            .ReturnsAsync(DataOutput<FinancialAccountLifecycleCommandOutput?>.New
                .WithData(new FinancialAccountLifecycleCommandOutput { Id = id }));
        var writer = new Mock<IAuditEntryWriter>();
        var handler = new AuditingCommandHandler<
            RestoreFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>(
            inner.Object,
            writer.Object,
            NullLogger<AuditingCommandHandler<
                RestoreFinancialAccountCommand,
                FinancialAccountLifecycleCommandOutput>>.Instance);

        await handler.HandleAsync(new RestoreFinancialAccountCommand());

        writer.Verify(entry => entry.WriteAsync(
            nameof(RestoreFinancialAccountCommand),
            "FinancialAccount",
            id,
            true,
            null), Times.Once);
    }

    [UnitFact]
    public async Task GivenHardDeleteCommand_WhenAudited_ThenCanonicalEntityTypeIsWritten()
    {
        var id = Guid.NewGuid();
        var inner = new Mock<ICommandHandlerAsync<
            HardDeleteFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>>();
        inner.Setup(handler => handler.HandleAsync(It.IsAny<HardDeleteFinancialAccountCommand>()))
            .ReturnsAsync(DataOutput<FinancialAccountLifecycleCommandOutput?>.New
                .WithData(new FinancialAccountLifecycleCommandOutput { Id = id }));
        var writer = new Mock<IAuditEntryWriter>();
        var handler = new AuditingCommandHandler<
            HardDeleteFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>(
            inner.Object,
            writer.Object,
            NullLogger<AuditingCommandHandler<
                HardDeleteFinancialAccountCommand,
                FinancialAccountLifecycleCommandOutput>>.Instance);

        await handler.HandleAsync(new HardDeleteFinancialAccountCommand());

        writer.Verify(entry => entry.WriteAsync(
            nameof(HardDeleteFinancialAccountCommand),
            "FinancialAccount",
            id,
            true,
            null), Times.Once);
    }

    [UnitFact]
    public async Task GivenThrowingHandler_WhenHandled_ThenRefusalIsWrittenAndExceptionRethrown()
    {
        var inner = new Mock<ICommandHandlerAsync<AuditStubCommand, AuditStubOutput>>();
        inner.Setup(handler => handler.HandleAsync(It.IsAny<AuditStubCommand>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var writer = new Mock<IAuditEntryWriter>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(inner, writer).HandleAsync(new AuditStubCommand()));

        writer.Verify(entry => entry.WriteAsync(
            nameof(AuditStubCommand),
            null,
            null,
            false,
            AuditEntryMessages.UnexpectedFailure), Times.Once);
    }

    [UnitFact]
    public async Task GivenThrowingHandlerAndFailingAudit_WhenHandled_ThenOriginalExceptionIsRethrown()
    {
        var inner = new Mock<ICommandHandlerAsync<AuditStubCommand, AuditStubOutput>>();
        inner.Setup(handler => handler.HandleAsync(It.IsAny<AuditStubCommand>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var writer = new Mock<IAuditEntryWriter>();
        writer.Setup(entry => entry.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<bool>(),
                It.IsAny<string?>()))
            .ThrowsAsync(new TimeoutException("audit unavailable"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(inner, writer).HandleAsync(new AuditStubCommand()));

        Assert.Equal("database down", exception.Message);
    }

    [UnitFact]
    public async Task GivenRefusedCommandWithId_WhenAudited_ThenCommandIdIsTheEntity()
    {
        var id = Guid.NewGuid();
        var inner = new Mock<ICommandHandlerAsync<
            DeleteFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>>();
        inner.Setup(handler => handler.HandleAsync(It.IsAny<DeleteFinancialAccountCommand>()))
            .ReturnsAsync(DataOutput<FinancialAccountLifecycleCommandOutput?>.New.WithError("not found"));
        var writer = new Mock<IAuditEntryWriter>();
        var handler = new AuditingCommandHandler<
            DeleteFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput>(
            inner.Object,
            writer.Object,
            NullLogger<AuditingCommandHandler<
                DeleteFinancialAccountCommand,
                FinancialAccountLifecycleCommandOutput>>.Instance);

        await handler.HandleAsync(new DeleteFinancialAccountCommand { Id = id });

        writer.Verify(entry => entry.WriteAsync(
            nameof(DeleteFinancialAccountCommand),
            "FinancialAccount",
            id,
            false,
            "not found"), Times.Once);
    }

    [UnitTheory]
    [InlineData(nameof(CreateFinancialAccountCommand), "FinancialAccount")]
    [InlineData(nameof(HardDeleteCreditCardCommand), "CreditCard")]
    [InlineData(nameof(SynchronizeConnectionCommand), "Connection")]
    [InlineData(nameof(ReauthenticateConnectionCommand), "Connection")]
    [InlineData(nameof(RecordInvestmentMovementCommand), "InvestmentMovement")]
    [InlineData(nameof(SettleCreditCardStatementCommand), "CreditCardStatement")]
    [InlineData(nameof(CloseCreditCardStatementCommand), "CreditCardStatement")]
    [InlineData(nameof(RecordManualExchangeRateCommand), "ExchangeRate")]
    [InlineData(nameof(MergeCounterpartiesCommand), "Counterparty")]
    [InlineData(nameof(AttachDocumentCommand), "Attachment")]
    [InlineData(nameof(RegenerateLocalAccountRecoveryCodesCommand), "LocalAccount")]
    [InlineData(nameof(EraseUserCommand), "User")]
    [InlineData(nameof(LoginThroughApiCommand), "Login")]
    [InlineData(nameof(AuditStubCommand), "AuditStub")]
    public void GivenCommandName_WhenEntityTypeResolved_ThenVerbAndSuffixAreRemoved(
        string commandName,
        string expected)
    {
        Assert.Equal(expected, AuditEntityTypes.Resolve(commandName));
    }

    private static AuditingCommandHandler<AuditStubCommand, AuditStubOutput> Handler(
        Mock<ICommandHandlerAsync<AuditStubCommand, AuditStubOutput>> inner,
        Mock<IAuditEntryWriter> writer) => new(
            inner.Object,
            writer.Object,
            NullLogger<AuditingCommandHandler<AuditStubCommand, AuditStubOutput>>.Instance);
}
