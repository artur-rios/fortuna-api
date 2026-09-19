using ArturRios.Fortuna.Command.Validation;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Fortuna.Shared.Auditing;
using FluentValidation;
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
            Audited(provider, provider.GetRequiredService<THandler>()));

        return services;
    }

    /// <summary>
    ///     Registers a write handler behind its validator and the audit decorator:
    ///     auditing wraps validation, so a command refused by its validator is still audited as a
    ///     refusal carrying the first validation message.
    /// </summary>
    public static IServiceCollection AddAuditedCommandHandler<TCommand, TOutput, THandler, TValidator>(
        this IServiceCollection services)
        where TCommand : BaseCommand
        where TOutput : CommandOutput
        where THandler : class, ICommandHandlerAsync<TCommand, TOutput>
        where TValidator : class, IValidator<TCommand>
    {
        services.AddScoped<IValidator<TCommand>, TValidator>();
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandlerAsync<TCommand, TOutput>>(provider =>
            Audited(provider, new ValidatingCommandHandler<TCommand, TOutput>(
                provider.GetRequiredService<THandler>(),
                provider.GetRequiredService<IValidator<TCommand>>())));

        return services;
    }

    private static AuditingCommandHandler<TCommand, TOutput> Audited<TCommand, TOutput>(
        IServiceProvider provider,
        ICommandHandlerAsync<TCommand, TOutput> inner)
        where TCommand : BaseCommand
        where TOutput : CommandOutput => new(
        inner,
        provider.GetRequiredService<IAuditEntryWriter>(),
        provider.GetRequiredService<ILogger<AuditingCommandHandler<TCommand, TOutput>>>());
}
