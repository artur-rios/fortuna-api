using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Validation;

/// <summary>
///     Decorates a write handler with its FluentValidation validator: an invalid command is
///     refused with every validation message, in the validator's order, before the inner handler
///     runs.
/// </summary>
public sealed class ValidatingCommandHandler<TCommand, TOutput>(
    ICommandHandlerAsync<TCommand, TOutput> inner,
    IValidator<TCommand> validator)
    : ICommandHandlerAsync<TCommand, TOutput>
    where TCommand : BaseCommand
    where TOutput : CommandOutput
{
    public async Task<DataOutput<TOutput?>> HandleAsync(TCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<TOutput?>.New.WithErrors(
                validation.Errors.Select(failure => failure.ErrorMessage));
        }

        return await inner.HandleAsync(command);
    }
}
