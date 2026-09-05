using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class ReassignCategoryTransactionsCommandHandler(
    IValidator<ReassignCategoryTransactionsCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICategoryTransactionReassigner categories,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<
        ReassignCategoryTransactionsCommand,
        ReassignCategoryTransactionsCommandOutput>
{
    public async Task<DataOutput<ReassignCategoryTransactionsCommandOutput?>> HandleAsync(
        ReassignCategoryTransactionsCommand command)
    {
        var output = DataOutput<ReassignCategoryTransactionsCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
        if (profile is null)
        {
            return output.WithError(CategoryMessages.ProfileNotFound);
        }

        var result = await categories.ReassignAsync(
            new CategoryTransactionReassignment(
                profile.Id,
                command.Id,
                command.TargetCategoryId,
                command.IncludeDescendants,
                timeProvider.GetUtcNow()),
            CancellationToken.None);

        if (result.Outcome != CategoryTransactionReassignmentOutcome.Succeeded)
        {
            return result.Outcome switch
            {
                CategoryTransactionReassignmentOutcome.CategoryNotFound =>
                    output.WithError(CategoryMessages.NotFound),
                CategoryTransactionReassignmentOutcome.SameCategory =>
                    output.WithError(CategoryMessages.SourceAndTargetMustDiffer),
                _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null)
            };
        }

        return output
            .WithData(new ReassignCategoryTransactionsCommandOutput
            {
                Id = command.Id,
                TargetCategoryId = command.TargetCategoryId,
                IncludeDescendants = command.IncludeDescendants,
                ReassignedCount = result.ReassignedCount
            })
            .WithMessage(CategoryMessages.TransactionsReassignedSuccessfully);
    }
}
