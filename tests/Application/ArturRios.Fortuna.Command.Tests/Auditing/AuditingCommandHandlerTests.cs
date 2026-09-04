using ArturRios.Fortuna.Command.Auditing;
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

    private static AuditingCommandHandler<AuditStubCommand, AuditStubOutput> Handler(
        Mock<ICommandHandlerAsync<AuditStubCommand, AuditStubOutput>> inner,
        Mock<IAuditEntryWriter> writer) => new(
            inner.Object,
            writer.Object,
            NullLogger<AuditingCommandHandler<AuditStubCommand, AuditStubOutput>>.Instance);
}
