using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class DownloadAttachmentQueryHandler(
    ICurrentProfileResolver profileResolver,
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
        var profile = await profileResolver.ResolveAsync();
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

            var read = await storage.OpenReadAsync(
                attachment.StorageKey,
                CancellationToken.None);
            if (read.Status == AttachmentReadStatus.NotFound)
            {
                await RecordDiscrepancyAsync(attachment.Id);

                return output.WithError(AttachmentMessages.StoredObjectNotFound);
            }

            if (!read.IsFound)
            {
                return output.WithError(AttachmentMessages.StorageUnavailable);
            }

            return output
                .WithData(new DownloadAttachmentQueryOutput
                {
                    Id = attachment.Id,
                    FileName = attachment.FileName,
                    ContentType = attachment.ContentType,
                    SizeInBytes = attachment.SizeInBytes,
                    Content = read.Content
                })
                .WithMessage(AttachmentMessages.DownloadedSuccessfully);
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
