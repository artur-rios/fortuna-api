using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RequestPersonalDataExportCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IPersonalDataExportStore exports,
    IBackgroundJobQueue queue,
    DataExportOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RequestPersonalDataExportCommand, RequestPersonalDataExportCommandOutput>
{
    public async Task<DataOutput<RequestPersonalDataExportCommandOutput?>> HandleAsync(
        RequestPersonalDataExportCommand command)
    {
        var actor = actorAccessor.Actor;
        var profile = actor?.RoleId == (int)HeimdallRoles.User
            ? actor.IsLocal
                ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None)
            : null;
        if (profile is null)
        {
            return DataOutput<RequestPersonalDataExportCommandOutput?>.New.WithError(
                PersonalDataExportMessages.ProfileNotFound);
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(options.Retention);
        var queued = await exports.QueuePersonalAsync(
            new QueuePersonalDataExportRequest(
                profile.Id,
                $"fortuna-personal-data-{now:yyyyMMddHHmmss}.zip",
                command.CorrelationId,
                now,
                expiresAt),
            CancellationToken.None);
        await queue.EnqueueAsync(queued.BackgroundJobId, CancellationToken.None);
        return DataOutput<RequestPersonalDataExportCommandOutput?>.New
            .WithData(new RequestPersonalDataExportCommandOutput
            {
                JobId = queued.ExportId,
                Status = DataExportStatus.Pending,
                Progress = 0,
                ExpiresAt = expiresAt
            })
            .WithMessage(PersonalDataExportMessages.Accepted);
    }
}
