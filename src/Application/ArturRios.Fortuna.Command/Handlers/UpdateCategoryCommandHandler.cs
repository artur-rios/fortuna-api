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

public sealed class UpdateCategoryCommandHandler(
    IValidator<UpdateCategoryCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ICategoryUpdater categories,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateCategoryCommand, UpdateCategoryCommandOutput>
{
    public async Task<DataOutput<UpdateCategoryCommandOutput?>> HandleAsync(
        UpdateCategoryCommand command)
    {
        var output = DataOutput<UpdateCategoryCommandOutput?>.New;
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

        var result = await categories.UpdateAsync(
            new CategoryUpdate(
                profile.Id,
                command.Id,
                command.Name.Trim(),
                command.ParentId,
                timeProvider.GetUtcNow()),
            CancellationToken.None);

        if (result.Outcome != CategoryUpdateOutcome.Succeeded)
        {
            return result.Outcome switch
            {
                CategoryUpdateOutcome.NotFound =>
                    output.WithError(CategoryMessages.NotFound),
                CategoryUpdateOutcome.ParentNotFound =>
                    output.WithError(CategoryMessages.ParentNotFound),
                CategoryUpdateOutcome.DuplicateSiblingName =>
                    output.WithError(CategoryMessages.DuplicateSiblingName),
                CategoryUpdateOutcome.CycleDetected =>
                    output.WithError(CategoryMessages.CycleDetected),
                _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null)
            };
        }

        var category = result.Category!;
        return output
            .WithData(new UpdateCategoryCommandOutput
            {
                Id = category.Id,
                Name = category.Name,
                ParentId = category.ParentId,
                CreatedAt = category.CreatedAt,
                UpdatedAt = category.UpdatedAt
            })
            .WithMessage(CategoryMessages.UpdatedSuccessfully);
    }
}
