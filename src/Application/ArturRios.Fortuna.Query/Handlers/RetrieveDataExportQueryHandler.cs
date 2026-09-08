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
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class RetrieveDataExportQueryHandler(
    IValidator<GetDataExportQuery> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IDataExportReader exports,
    IAttachmentStore storage,
    TimeProvider timeProvider,
    ILogger<RetrieveDataExportQueryHandler> logger)
    : IQueryHandlerAsync<GetDataExportQuery, RetrieveDataExportQueryOutput>
{
    public async Task<DataOutput<RetrieveDataExportQueryOutput?>> HandleAsync(
        GetDataExportQuery query)
    {
        var output = DataOutput<RetrieveDataExportQueryOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(error => error.ErrorMessage));
        }

        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(
                    actor.SubjectId,
                    CancellationToken.None);
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

        var state = Project(export);
        if (export.Status != DataExportStatus.Completed)
        {
            return output.WithData(state).WithMessage(
                DataExportMessages.RetrievedSuccessfully);
        }

        if (timeProvider.GetUtcNow() >= export.ExpiresAt)
        {
            return output.WithError(DataExportMessages.Expired);
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

            var content = await storage.OpenReadAsync(
                export.StorageKey,
                CancellationToken.None);
            return output.WithData(Project(export, content)).WithMessage(
                DataExportMessages.RetrievedSuccessfully);
        }
        catch (AttachmentObjectNotFoundException)
        {
            return output.WithError(DataExportMessages.FileNotFound);
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
