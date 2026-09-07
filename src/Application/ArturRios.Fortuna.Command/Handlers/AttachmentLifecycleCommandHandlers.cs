using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class DeleteAttachmentCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IAttachmentLifecycleStore attachments,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeleteAttachmentCommand, AttachmentLifecycleCommandOutput>
{
    public async Task<DataOutput<AttachmentLifecycleCommandOutput?>> HandleAsync(
        DeleteAttachmentCommand command)
    {
        var profile = await AttachmentLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return AttachmentLifecycleHandler.ProfileNotFound();
        }

        var result = await attachments.SoftDeleteAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        return AttachmentLifecycleHandler.Resolve(
            result,
            AttachmentMessages.DeletedSuccessfully);
    }
}

public sealed class HardDeleteAttachmentCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IAttachmentLifecycleStore attachments)
    : ICommandHandlerAsync<HardDeleteAttachmentCommand, AttachmentLifecycleCommandOutput>
{
    public async Task<DataOutput<AttachmentLifecycleCommandOutput?>> HandleAsync(
        HardDeleteAttachmentCommand command)
    {
        var profile = await AttachmentLifecycleHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return AttachmentLifecycleHandler.ProfileNotFound();
        }

        var result = await attachments.HardDeleteAsync(
            profile.Id,
            command.Id,
            CancellationToken.None);
        return AttachmentLifecycleHandler.Resolve(
            result,
            AttachmentMessages.HardDeletedSuccessfully);
    }
}

internal static class AttachmentLifecycleHandler
{
    public static async Task<UserProfileSnapshot?> ResolveProfileAsync(
        RequestActor? actor,
        IUserProfileReader profiles) => actor?.IsLocal == true
        ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
        : actor is null
            ? null
            : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    public static DataOutput<AttachmentLifecycleCommandOutput?> ProfileNotFound() =>
        DataOutput<AttachmentLifecycleCommandOutput?>.New
            .WithError(AttachmentMessages.ProfileNotFound);

    public static DataOutput<AttachmentLifecycleCommandOutput?> Resolve(
        AttachmentLifecycleResult result,
        string successMessage)
    {
        var output = DataOutput<AttachmentLifecycleCommandOutput?>.New;
        return result.Outcome switch
        {
            AttachmentLifecycleOutcome.Succeeded => output
                .WithData(new AttachmentLifecycleCommandOutput
                {
                    Id = result.Id!.Value,
                    IsDeleted = result.IsDeleted ?? false
                })
                .WithMessage(successMessage),
            AttachmentLifecycleOutcome.NotFound => output
                .WithError(AttachmentMessages.AttachmentNotFound),
            AttachmentLifecycleOutcome.HardDeleteRequiresSoftDeletion => output
                .WithError(AttachmentMessages.HardDeleteRequiresSoftDeletion),
            AttachmentLifecycleOutcome.StorageUnavailable => output
                .WithError(AttachmentMessages.StorageUnavailable),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
