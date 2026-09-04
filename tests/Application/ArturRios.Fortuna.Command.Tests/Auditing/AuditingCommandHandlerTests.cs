using ArturRios.Fortuna.Command.Auditing;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
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

    private static AuditingCommandHandler<AuditStubCommand, AuditStubOutput> Handler(
        Mock<ICommandHandlerAsync<AuditStubCommand, AuditStubOutput>> inner,
        Mock<IAuditEntryWriter> writer) => new(
            inner.Object,
            writer.Object,
            NullLogger<AuditingCommandHandler<AuditStubCommand, AuditStubOutput>>.Instance);
}
