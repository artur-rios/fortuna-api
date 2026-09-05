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

public sealed class MaterializeRecurringTransactionsCommandHandler(
    IValidator<MaterializeRecurringTransactionsCommand> validator,
    IRequestActorAccessor actors,
    IUserProfileReader profiles,
    IRecurringTransactionMaterializer materializer,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<MaterializeRecurringTransactionsCommand,
        MaterializeRecurringTransactionsCommandOutput>
{
    public async Task<DataOutput<MaterializeRecurringTransactionsCommandOutput?>> HandleAsync(
        MaterializeRecurringTransactionsCommand command)
    {
        var output = DataOutput<MaterializeRecurringTransactionsCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(error => error.ErrorMessage));
        }

        var actor = actors.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
        if (profile is null)
        {
            return output.WithError(RecurringTransactionMessages.ProfileNotFound);
        }

        var now = timeProvider.GetUtcNow();
        var through = DateOnly.FromDateTime(now.UtcDateTime);
        var result = await materializer.MaterializeAsync(
            new RecurringMaterializationRun(profile.Id, through, now),
            CancellationToken.None);
        return output.WithData(new MaterializeRecurringTransactionsCommandOutput
        {
            MaterializedThrough = through,
            CreatedCount = result.CreatedCount,
            PossibleDuplicateCount = result.PossibleDuplicateCount,
            Rules = result.Rules.Select(rule => new RecurringRuleMaterializationCommandOutput
            {
                RuleId = rule.RuleId,
                CreatedCount = rule.CreatedCount,
                PossibleDuplicateCount = rule.PossibleDuplicateCount,
                IsComplete = rule.IsComplete,
                SkipReason = rule.SkipReason?.ToString(),
                Occurrences = rule.Occurrences.Select(occurrence =>
                    new RecurringOccurrenceMaterializationCommandOutput
                    {
                        OccurredOn = occurrence.OccurredOn,
                        TransactionId = occurrence.TransactionId,
                        IsPossibleDuplicate = occurrence.IsPossibleDuplicate,
                        Error = occurrence.Error
                    }).ToArray()
            }).ToArray()
        }).WithMessage(RecurringTransactionMessages.MaterializedSuccessfully);
    }
}
