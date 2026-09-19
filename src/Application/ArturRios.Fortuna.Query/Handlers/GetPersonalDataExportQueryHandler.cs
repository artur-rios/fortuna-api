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

public sealed class GetPersonalDataExportQueryHandler(
    ICurrentProfileResolver profileResolver,
    IPersonalDataExportStore exports,
    IAttachmentStore storage,
    TimeProvider timeProvider,
    ILogger<GetPersonalDataExportQueryHandler> logger)
    : IQueryHandlerAsync<GetPersonalDataExportQuery, PersonalDataExportQueryOutput>
{
    public async Task<DataOutput<PersonalDataExportQueryOutput?>> HandleAsync(
        GetPersonalDataExportQuery query)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                PersonalDataExportMessages.ProfileNotFound);
        }

        var export = await exports.FindOwnedPersonalAsync(
            profile.Id,
            query.JobId,
            CancellationToken.None);
        if (export is null)
        {
            return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                PersonalDataExportMessages.NotFound);
        }

        if (ExportExpiry.HasExpired(export, timeProvider))
        {
            return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                PersonalDataExportMessages.Expired);
        }

        if (export.Status != DataExportStatus.Completed)
        {
            return DataOutput<PersonalDataExportQueryOutput?>.New
                .WithData(Project(export))
                .WithMessage(PersonalDataExportMessages.RetrievedSuccessfully);
        }

        if (string.IsNullOrWhiteSpace(export.StorageKey) ||
            string.IsNullOrWhiteSpace(export.ContentType))
        {
            return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                PersonalDataExportMessages.FileNotFound);
        }

        try
        {
            if (!await storage.IsHealthyAsync(CancellationToken.None))
            {
                return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                    PersonalDataExportMessages.StorageUnavailable);
            }

            var read = await storage.OpenReadAsync(export.StorageKey, CancellationToken.None);
            if (!read.IsFound)
            {
                return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                    read.Status == AttachmentReadStatus.NotFound
                        ? PersonalDataExportMessages.FileNotFound
                        : PersonalDataExportMessages.StorageUnavailable);
            }

            return DataOutput<PersonalDataExportQueryOutput?>.New
                .WithData(Project(export, read.Content))
                .WithMessage(PersonalDataExportMessages.RetrievedSuccessfully);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Personal data archive storage read failed");

            return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                PersonalDataExportMessages.StorageUnavailable);
        }
    }

    private static PersonalDataExportQueryOutput Project(
        DataExportReadSnapshot export,
        Stream? content = null) => new()
        {
            JobId = export.Id,
            Status = export.Status,
            Progress = export.Status switch
            {
                DataExportStatus.Pending => 0,
                DataExportStatus.Running => 50,
                _ => 100
            },
            FileName = export.FileName,
            ContentType = export.ContentType,
            FailureReason = export.Status == DataExportStatus.Failed
                ? export.FailureReason ?? PersonalDataExportMessages.GenerationFailed
                : null,
            CreatedAt = export.CreatedAt,
            UpdatedAt = export.UpdatedAt,
            ExpiresAt = export.ExpiresAt,
            Content = content
        };
}
