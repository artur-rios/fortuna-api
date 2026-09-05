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

public sealed class DefineRecurringTransactionCommandHandler(
    IValidator<DefineRecurringTransactionCommand> validator,
    IRequestActorAccessor actors,
    IUserProfileReader profiles,
    IRecurringTransactionStore rules,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DefineRecurringTransactionCommand, DefineRecurringTransactionCommandOutput>
{
    public async Task<DataOutput<DefineRecurringTransactionCommandOutput?>> HandleAsync(
        DefineRecurringTransactionCommand command)
    {
        var output = DataOutput<DefineRecurringTransactionCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid) return output.WithErrors(validation.Errors.Select(item => item.ErrorMessage));
        var actor = actors.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null ? null : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
        if (profile is null) return output.WithError(RecurringTransactionMessages.ProfileNotFound);
        var now = timeProvider.GetUtcNow();
        var result = await rules.RecordAsync(new RecurringTransactionRecord(
            profile.Id, command.FinancialAccountId, command.CreditCardId, command.CategoryId,
            command.Direction, command.Amount, command.Frequency, command.StartsOn, command.EndsOn,
            command.Description, command.Counterparty, DateOnly.FromDateTime(now.UtcDateTime), now),
            CancellationToken.None);
        if (result.Rule is null)
        {
            return output.WithError(result.Outcome switch
            {
                RecurringTransactionRecordOutcome.FinancialAccountNotFound => RecurringTransactionMessages.FinancialAccountNotFound,
                RecurringTransactionRecordOutcome.CreditCardNotFound => RecurringTransactionMessages.CreditCardNotFound,
                RecurringTransactionRecordOutcome.CategoryNotFound => RecurringTransactionMessages.CategoryNotFound,
                _ => throw new InvalidOperationException("Unknown recurring transaction outcome.")
            });
        }

        var rule = result.Rule;
        return output.WithData(new DefineRecurringTransactionCommandOutput
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
            Description = rule.Description,
            CounterpartyId = rule.CounterpartyId,
            CounterpartyName = rule.CounterpartyName,
            NextOccurrences = rule.NextOccurrences,
            CreatedAt = rule.CreatedAt
        }).WithMessage(RecurringTransactionMessages.RecordedSuccessfully);
    }
}
