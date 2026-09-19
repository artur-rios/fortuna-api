using ArturRios.Fortuna.Command.Auditing;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using ArturRios.Util.Test.Attributes;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArturRios.Fortuna.Command.Tests.Auditing;

public sealed class ValidatedCommandHandlerRegistrationTests
{
    [UnitFact]
    public async Task GivenInvalidCommand_WhenHandled_ThenEveryErrorIsReturnedInOrderAndAuditedAsRefusal()
    {
        var writer = new Mock<IAuditEntryWriter>();
        var inner = new CountingHandler();
        var handler = Resolve(writer, inner);

        var result = await handler.HandleAsync(new AuditStubCommand());

        Assert.False(result.Success);
        Assert.Equal(["first", "second"], result.Errors);
        Assert.Equal(0, inner.Calls);
        writer.Verify(entry => entry.WriteAsync(
            nameof(AuditStubCommand),
            null,
            null,
            false,
            "first"), Times.Once);
    }

    [UnitFact]
    public async Task GivenValidCommand_WhenHandled_ThenInnerHandlerRunsAndSuccessIsAudited()
    {
        var writer = new Mock<IAuditEntryWriter>();
        var inner = new CountingHandler();
        var handler = Resolve(writer, inner, valid: true);

        var result = await handler.HandleAsync(new AuditStubCommand());

        Assert.True(result.Success);
        Assert.Equal(1, inner.Calls);
        writer.Verify(entry => entry.WriteAsync(
            nameof(AuditStubCommand),
            "AuditStub",
            CountingHandler.EntityId,
            true,
            null), Times.Once);
    }

    private static ICommandHandlerAsync<AuditStubCommand, AuditStubOutput> Resolve(
        Mock<IAuditEntryWriter> writer,
        CountingHandler inner,
        bool valid = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(writer.Object);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(new ValidityFlag(valid));
        services.AddAuditedCommandHandler<AuditStubCommand, AuditStubOutput, CountingHandler,
            StubValidator>();
        services.AddSingleton(inner);

        return services.BuildServiceProvider()
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<ICommandHandlerAsync<AuditStubCommand, AuditStubOutput>>();
    }

    private sealed record ValidityFlag(bool Valid);

    private sealed class StubValidator : AbstractValidator<AuditStubCommand>
    {
        public StubValidator(ValidityFlag flag)
        {
            if (flag.Valid)
            {
                return;
            }

            RuleFor(command => command).Must(_ => false).WithMessage("first");
            RuleFor(command => command).Must(_ => false).WithMessage("second");
        }
    }

    private sealed class CountingHandler : ICommandHandlerAsync<AuditStubCommand, AuditStubOutput>
    {
        public static readonly Guid EntityId = Guid.NewGuid();

        public int Calls { get; private set; }

        public Task<DataOutput<AuditStubOutput?>> HandleAsync(AuditStubCommand command)
        {
            Calls++;

            return Task.FromResult(DataOutput<AuditStubOutput?>.New.WithData(
                new AuditStubOutput { Id = EntityId }));
        }
    }
}
