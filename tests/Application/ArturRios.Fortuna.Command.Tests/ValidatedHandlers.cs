using ArturRios.Fortuna.Command.Validation;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Tests;

/// <summary>Puts a handler behind its validator, the way the application registers it.</summary>
internal static class ValidatedHandlers
{
    public static ICommandHandlerAsync<TCommand, TOutput> Validated<TCommand, TOutput>(
        this ICommandHandlerAsync<TCommand, TOutput> handler,
        IValidator<TCommand> validator)
        where TCommand : BaseCommand
        where TOutput : CommandOutput => new ValidatingCommandHandler<TCommand, TOutput>(handler, validator);
}
