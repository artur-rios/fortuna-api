using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetDataExportQueryHandler(
    ICurrentProfileResolver profileResolver,
    IDataExportReader exports,
    IAttachmentStore storage,
    TimeProvider timeProvider,
    ILogger<GetDataExportQueryHandler> logger)
    : IQueryHandlerAsync<GetDataExportQuery, RetrieveDataExportQueryOutput>
{
    public async Task<DataOutput<RetrieveDataExportQueryOutput?>> HandleAsync(
        GetDataExportQuery query)
    {
        var output = DataOutput<RetrieveDataExportQueryOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(DataExportMessages.ProfileNotFound);
        }

        var export = await exports.FindOwnedAsync(
            profile.Id,
            query.Id,
            CancellationToken.None);
        if (export is null)
        {
            return output.WithError(DataExportMessages.NotFound);
        }

        if (ExportExpiry.HasExpired(export, timeProvider))
        {
            return output.WithError(DataExportMessages.Expired);
        }

        if (export.Status != DataExportStatus.Completed)
        {
            return output.WithData(Project(export)).WithMessage(
                DataExportMessages.RetrievedSuccessfully);
        }

        if (string.IsNullOrWhiteSpace(export.StorageKey) ||
            string.IsNullOrWhiteSpace(export.ContentType))
        {
            return output.WithError(DataExportMessages.FileNotFound);
        }

        try
        {
            if (!await storage.IsHealthyAsync(CancellationToken.None))
            {
                return output.WithError(DataExportMessages.StorageUnavailable);
            }

            var read = await storage.OpenReadAsync(
                export.StorageKey,
                CancellationToken.None);
            if (!read.IsFound)
            {
                return output.WithError(read.Status == AttachmentReadStatus.NotFound
                    ? DataExportMessages.FileNotFound
                    : DataExportMessages.StorageUnavailable);
            }

            return output.WithData(Project(export, read.Content)).WithMessage(
                DataExportMessages.RetrievedSuccessfully);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Export storage read failed");

            return output.WithError(DataExportMessages.StorageUnavailable);
        }
    }

    private static RetrieveDataExportQueryOutput Project(
        DataExportReadSnapshot export,
        Stream? content = null) => new()
        {
            Id = export.Id,
            JobId = export.BackgroundJobId,
            Format = export.Format,
            Status = export.Status,
            FileName = export.FileName,
            RowCount = export.RowCount,
            ContentType = export.ContentType,
            FailureReason = export.Status == DataExportStatus.Failed
                ? export.FailureReason ?? DataExportMessages.GenerationFailed
                : null,
            CreatedAt = export.CreatedAt,
            UpdatedAt = export.UpdatedAt,
            ExpiresAt = export.ExpiresAt,
            Content = content
        };
}
