using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetPersonalDataExportQueryHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IPersonalDataExportStore exports,
    IAttachmentStore storage,
    TimeProvider timeProvider,
    ILogger<GetPersonalDataExportQueryHandler> logger)
    : IQueryHandlerAsync<GetPersonalDataExportQuery, PersonalDataExportQueryOutput>
{
    public async Task<DataOutput<PersonalDataExportQueryOutput?>> HandleAsync(
        GetPersonalDataExportQuery query)
    {
        if (query.JobId == Guid.Empty)
        {
            return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                PersonalDataExportMessages.NotFound);
        }
        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
        if (profile is null)
        {
            return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                PersonalDataExportMessages.ProfileNotFound);
        }
        var export = await exports.FindOwnedPersonalAsync(
            profile.Id,
            query.JobId,
            CancellationToken.None);
        if (export is null || timeProvider.GetUtcNow() >= export.ExpiresAt)
        {
            return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                export is null
                    ? PersonalDataExportMessages.NotFound
                    : PersonalDataExportMessages.Expired);
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
            var content = await storage.OpenReadAsync(export.StorageKey, CancellationToken.None);
            return DataOutput<PersonalDataExportQueryOutput?>.New
                .WithData(Project(export, content))
                .WithMessage(PersonalDataExportMessages.RetrievedSuccessfully);
        }
        catch (AttachmentObjectNotFoundException)
        {
            return DataOutput<PersonalDataExportQueryOutput?>.New.WithError(
                PersonalDataExportMessages.FileNotFound);
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
