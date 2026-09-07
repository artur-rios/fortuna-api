using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class DownloadAttachmentQueryHandler(
    IValidator<DownloadAttachmentQuery> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IAttachmentMetadataReader metadata,
    IAttachmentStore storage,
    IAuditEntryWriter auditEntries,
    ILogger<DownloadAttachmentQueryHandler> logger)
    : IQueryHandlerAsync<DownloadAttachmentQuery, DownloadAttachmentQueryOutput>
{
    public async Task<DataOutput<DownloadAttachmentQueryOutput?>> HandleAsync(
        DownloadAttachmentQuery query)
    {
        var output = DataOutput<DownloadAttachmentQueryOutput?>.New;
        var validation = await validator.ValidateAsync(query);
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

        var attachment = await metadata.FindOwnedAsync(
            profile.Id,
            query.Id,
            CancellationToken.None);
        if (attachment is null)
        {
            return output.WithError(AttachmentMessages.AttachmentNotFound);
        }

        try
        {
            if (!await storage.IsHealthyAsync(CancellationToken.None))
            {
                return output.WithError(AttachmentMessages.StorageUnavailable);
            }

            var content = await storage.OpenReadAsync(
                attachment.StorageKey,
                CancellationToken.None);
            return output
                .WithData(new DownloadAttachmentQueryOutput
                {
                    Id = attachment.Id,
                    FileName = attachment.FileName,
                    ContentType = attachment.ContentType,
                    SizeInBytes = attachment.SizeInBytes,
                    Content = content
                })
                .WithMessage(AttachmentMessages.DownloadedSuccessfully);
        }
        catch (AttachmentObjectNotFoundException)
        {
            await RecordDiscrepancyAsync(attachment.Id);
            return output.WithError(AttachmentMessages.StoredObjectNotFound);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Attachment storage read failed");
            return output.WithError(AttachmentMessages.StorageUnavailable);
        }
    }

    private async Task RecordDiscrepancyAsync(Guid attachmentId)
    {
        try
        {
            await auditEntries.WriteAsync(
                nameof(DownloadAttachmentQuery),
                "Attachment",
                attachmentId,
                false,
                AttachmentMessages.StoredObjectNotFound);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to audit missing attachment object");
        }
    }
}
