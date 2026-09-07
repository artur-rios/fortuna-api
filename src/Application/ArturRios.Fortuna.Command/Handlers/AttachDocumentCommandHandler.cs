using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class AttachDocumentCommandHandler(
    IValidator<AttachDocumentCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IAttachmentMetadataStore metadata,
    IAttachmentStore storage,
    TimeProvider timeProvider,
    ILogger<AttachDocumentCommandHandler> logger)
    : ICommandHandlerAsync<AttachDocumentCommand, AttachDocumentCommandOutput>
{
    public async Task<DataOutput<AttachDocumentCommandOutput?>> HandleAsync(
        AttachDocumentCommand command)
    {
        var output = DataOutput<AttachDocumentCommandOutput?>.New;
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
            return output.WithError(AttachmentMessages.ProfileNotFound);
        }

        if (!await metadata.IsOwnedLiveTransactionAsync(
                profile.Id,
                command.TransactionId,
                CancellationToken.None))
        {
            return output.WithError(AttachmentMessages.TransactionNotFound);
        }

        try
        {
            if (!await storage.IsHealthyAsync(CancellationToken.None))
            {
                return output.WithError(AttachmentMessages.StorageUnavailable);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Attachment storage health check failed");
            return output.WithError(AttachmentMessages.StorageUnavailable);
        }

        var key = $"attachments/{profile.Id:N}/{Guid.NewGuid():N}";
        try
        {
            await using var content = new MemoryStream(command.Content, writable: false);
            await storage.WriteAsync(key, content, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Attachment storage write failed");
            await TryDeleteAsync(key);
            return output.WithError(AttachmentMessages.StorageUnavailable);
        }

        AttachmentMetadataResult result;
        try
        {
            result = await metadata.CreateAsync(
                new AttachmentMetadataWrite(
                    profile.Id,
                    command.TransactionId,
                    command.FileName.Trim(),
                    command.ContentType.Trim().ToLowerInvariant(),
                    command.Content.LongLength,
                    key,
                    timeProvider.GetUtcNow()),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Attachment metadata persistence failed");
            await TryDeleteAsync(key);
            return output.WithError(AttachmentMessages.PersistenceFailed);
        }

        if (result.Outcome != AttachmentMetadataOutcome.Succeeded || result.Attachment is null)
        {
            await TryDeleteAsync(key);
            return output.WithError(AttachmentMessages.TransactionNotFound);
        }

        var attachment = result.Attachment;
        return output
            .WithData(new AttachDocumentCommandOutput
            {
                Id = attachment.Id,
                TransactionId = attachment.TransactionId,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeInBytes = attachment.SizeInBytes,
                CreatedAt = attachment.CreatedAt
            })
            .WithMessage(AttachmentMessages.AttachedSuccessfully);
    }

    private async Task TryDeleteAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to compensate attachment storage write");
        }
    }
}
