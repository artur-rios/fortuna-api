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

public sealed class CreateTagCommandHandler(
    IValidator<CreateTagCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITagStore tags,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateTagCommand, TagCommandOutput>
{
    public async Task<DataOutput<TagCommandOutput?>> HandleAsync(CreateTagCommand command)
    {
        var output = DataOutput<TagCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await TagHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return output.WithError(TagMessages.ProfileNotFound);
        }

        var result = await tags.CreateAsync(
            new TagCreation(profile.Id, command.Name, timeProvider.GetUtcNow()),
            CancellationToken.None);
        return TagHandler.Resolve(result.Tag, result.Outcome, TagMessages.CreatedSuccessfully);
    }
}

public sealed class UpdateTagCommandHandler(
    IValidator<UpdateTagCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITagUpdater tags,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateTagCommand, TagCommandOutput>
{
    public async Task<DataOutput<TagCommandOutput?>> HandleAsync(UpdateTagCommand command)
    {
        var output = DataOutput<TagCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await TagHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return output.WithError(TagMessages.ProfileNotFound);
        }

        var result = await tags.UpdateAsync(
            new TagUpdate(profile.Id, command.Id, command.Name, timeProvider.GetUtcNow()),
            CancellationToken.None);
        return TagHandler.Resolve(result.Tag, result.Outcome, TagMessages.UpdatedSuccessfully);
    }
}

public sealed class DeleteTagCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITagLifecycleStore tags,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteTagCommand, TagCommandOutput>
{
    public async Task<DataOutput<TagCommandOutput?>> HandleAsync(DeleteTagCommand command)
    {
        var profile = await TagHandler.ResolveProfileAsync(actorAccessor.Actor, profiles);
        if (profile is null)
        {
            return DataOutput<TagCommandOutput?>.New.WithError(TagMessages.ProfileNotFound);
        }

        var result = await tags.SoftDeleteAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return TagHandler.Resolve(
            result.Tag,
            result.Outcome,
            TagMessages.DeletedSuccessfully,
            result.DetachedTransactionCount);
    }
}

public sealed class AttachTransactionTagCommandHandler(
    IValidator<AttachTransactionTagCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITransactionTagStore tags,
    TagOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<AttachTransactionTagCommand, TransactionTagCommandOutput>
{
    public async Task<DataOutput<TransactionTagCommandOutput?>> HandleAsync(
        AttachTransactionTagCommand command) => await TransactionTagHandler.HandleAsync(
        command.Id,
        command.TagId,
        validator,
        command,
        actorAccessor.Actor,
        profiles,
        tags.AttachAsync,
        options,
        timeProvider,
        attaching: true);
}

public sealed class DetachTransactionTagCommandHandler(
    IValidator<DetachTransactionTagCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITransactionTagStore tags,
    TagOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DetachTransactionTagCommand, TransactionTagCommandOutput>
{
    public async Task<DataOutput<TransactionTagCommandOutput?>> HandleAsync(
        DetachTransactionTagCommand command) => await TransactionTagHandler.HandleAsync(
        command.Id,
        command.TagId,
        validator,
        command,
        actorAccessor.Actor,
        profiles,
        tags.DetachAsync,
        options,
        timeProvider,
        attaching: false);
}

internal static class TransactionTagHandler
{
    public static async Task<DataOutput<TransactionTagCommandOutput?>> HandleAsync<TCommand>(
        Guid transactionId,
        Guid tagId,
        IValidator<TCommand> validator,
        TCommand command,
        RequestActor? actor,
        IUserProfileReader profiles,
        Func<TransactionTagAssignment, CancellationToken,
            Task<TransactionTagAssignmentResult>> operation,
        TagOptions options,
        TimeProvider timeProvider,
        bool attaching)
    {
        var output = DataOutput<TransactionTagCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await TagHandler.ResolveProfileAsync(actor, profiles);
        if (profile is null)
        {
            return output.WithError(TagMessages.ProfileNotFound);
        }

        var result = await operation(
            new TransactionTagAssignment(
                profile.Id,
                transactionId,
                tagId,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (result.Outcome == TransactionTagAssignmentOutcome.NotFound)
        {
            return output.WithError(TagMessages.AssignmentNotFound);
        }

        if (result.Outcome == TransactionTagAssignmentOutcome.MaximumExceeded)
        {
            return output.WithErrors([
                TagMessages.MaximumExceeded,
                TagMessages.MaximumAllowed(options.MaximumPerTransaction)
            ]);
        }

        return output
            .WithData(new TransactionTagCommandOutput
            {
                Id = result.TransactionId!.Value,
                TagId = result.TagId!.Value,
                IsAttached = result.IsAttached,
                TagCount = result.TagCount
            })
            .WithMessage(attaching
                ? result.Changed
                    ? TagMessages.AttachedSuccessfully
                    : TagMessages.AlreadyAttached
                : result.Changed
                    ? TagMessages.DetachedSuccessfully
                    : TagMessages.AlreadyDetached);
    }
}

internal static class TagHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static DataOutput<TagCommandOutput?> Resolve(
        TagSnapshot? tag,
        TagMutationOutcome outcome,
        string successMessage,
        int detachedTransactionCount = 0)
    {
        var output = DataOutput<TagCommandOutput?>.New;
        return outcome switch
        {
            TagMutationOutcome.Succeeded => output
                .WithData(new TagCommandOutput
                {
                    Id = tag!.Id,
                    Name = tag.Name,
                    IsDeleted = tag.IsDeleted,
                    DetachedTransactionCount = detachedTransactionCount,
                    CreatedAt = tag.CreatedAt,
                    UpdatedAt = tag.UpdatedAt
                })
                .WithMessage(successMessage),
            TagMutationOutcome.NotFound => output.WithError(TagMessages.NotFound),
            TagMutationOutcome.DuplicateName => output.WithError(TagMessages.DuplicateName),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
    }
}
