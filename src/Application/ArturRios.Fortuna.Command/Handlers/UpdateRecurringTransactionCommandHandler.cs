using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class UpdateRecurringTransactionCommandHandler(
    IValidator<UpdateRecurringTransactionCommand> validator,
    IRequestActorAccessor actors,
    IUserProfileReader profiles,
    IRecurringTransactionUpdater rules,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateRecurringTransactionCommand, UpdateRecurringTransactionCommandOutput>
{
    public async Task<DataOutput<UpdateRecurringTransactionCommandOutput?>> HandleAsync(
        UpdateRecurringTransactionCommand command)
    {
        var output = DataOutput<UpdateRecurringTransactionCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await RecurringTransactionHandler.ResolveProfileAsync(actors.Actor, profiles);
        if (profile is null)
        {
            return output.WithError(RecurringTransactionMessages.ProfileNotFound);
        }

        var now = timeProvider.GetUtcNow();
        var result = await rules.UpdateAsync(new RecurringTransactionUpdate(
            profile.Id,
            command.Id,
            command.FinancialAccountId,
            command.CreditCardId,
            command.CategoryId,
            command.Direction,
            command.Amount,
            command.Frequency,
            command.StartsOn,
            command.EndsOn,
            command.Description,
            command.Counterparty,
            DateOnly.FromDateTime(now.UtcDateTime),
            now), CancellationToken.None);
        if (result.Rule is null)
        {
            return output.WithError(result.Outcome switch
            {
                RecurringTransactionUpdateOutcome.NotFound => RecurringTransactionMessages.NotFound,
                RecurringTransactionUpdateOutcome.FinancialAccountNotFound =>
                    RecurringTransactionMessages.FinancialAccountNotFound,
                RecurringTransactionUpdateOutcome.CreditCardNotFound =>
                    RecurringTransactionMessages.CreditCardNotFound,
                RecurringTransactionUpdateOutcome.CategoryNotFound =>
                    RecurringTransactionMessages.CategoryNotFound,
                _ => throw new InvalidOperationException("Unknown recurring transaction update outcome.")
            });
        }

        var rule = result.Rule;
        return output.WithData(new UpdateRecurringTransactionCommandOutput
        {
            Id = rule.Id,
            FinancialAccountId = rule.FinancialAccountId,
            CreditCardId = rule.CreditCardId,
            CategoryId = rule.CategoryId,
            Direction = rule.Direction,
            Amount = rule.Amount,
            CurrencyCode = rule.CurrencyCode,
            Frequency = rule.Frequency,
            StartsOn = rule.StartsOn,
            EndsOn = rule.EndsOn,
            LastMaterializedOn = rule.LastMaterializedOn,
            Description = rule.Description,
            CounterpartyId = rule.CounterpartyId,
            CounterpartyName = rule.CounterpartyName,
            AppliesFrom = rule.NextOccurrences.Select(date => (DateOnly?)date).FirstOrDefault(),
            MaterializedOccurrencesChanged = false,
            NextOccurrences = rule.NextOccurrences,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        }).WithMessage(RecurringTransactionMessages.UpdatedSuccessfully);
    }
}
