using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Command.Auditing;

public static class CommandHandlerRegistrationExtensions
{
    public static IServiceCollection AddAuditedCommandHandler<TCommand, TOutput, THandler>(
        this IServiceCollection services)
        where TCommand : BaseCommand
        where TOutput : CommandOutput
        where THandler : class, ICommandHandlerAsync<TCommand, TOutput>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandlerAsync<TCommand, TOutput>>(provider =>
            new AuditingCommandHandler<TCommand, TOutput>(
                provider.GetRequiredService<THandler>(),
                provider.GetRequiredService<IAuditEntryWriter>(),
                provider.GetRequiredService<ILogger<AuditingCommandHandler<TCommand, TOutput>>>()));

        return services;
    }
}
