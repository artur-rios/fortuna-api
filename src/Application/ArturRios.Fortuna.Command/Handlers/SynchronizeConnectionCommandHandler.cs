using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class SynchronizeConnectionCommandHandler(
    IValidator<SynchronizeConnectionCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IPluggySynchronizationStore synchronizations,
    IBackgroundJobQueue queue,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<SynchronizeConnectionCommand, SynchronizeConnectionCommandOutput>
{
    public async Task<DataOutput<SynchronizeConnectionCommandOutput?>> HandleAsync(
        SynchronizeConnectionCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<SynchronizeConnectionCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return DataOutput<SynchronizeConnectionCommandOutput?>.New.WithError(
                PluggySynchronizationMessages.ProfileNotFound);
        }

        var result = await synchronizations.QueueAsync(
            profile.Id,
            command.Id,
            command.PeriodStart,
            command.PeriodEnd,
            command.CorrelationId,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        if (result.Outcome == QueueSynchronizationOutcome.Succeeded)
        {
            await queue.EnqueueAsync(result.BackgroundJobId!.Value, CancellationToken.None);
        }

        return Resolve(result);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private static DataOutput<SynchronizeConnectionCommandOutput?> Resolve(
        QueueSynchronizationResult result)
    {
        var output = DataOutput<SynchronizeConnectionCommandOutput?>.New;
        if (result.Job is not null)
        {
            output = output.WithData(new SynchronizeConnectionCommandOutput
            {
                ImportJobId = result.Job.Id,
                Status = result.Job.Status,
                PeriodStart = result.Job.PeriodStart,
                PeriodEnd = result.Job.PeriodEnd
            });
        }

        return result.Outcome switch
        {
            QueueSynchronizationOutcome.Succeeded => output.WithMessage(
                PluggySynchronizationMessages.Accepted),
            QueueSynchronizationOutcome.ConnectionNotFound => output.WithError(
                PluggySynchronizationMessages.ConnectionNotFound),
            QueueSynchronizationOutcome.ConnectionInactive => output.WithError(
                PluggySynchronizationMessages.ConnectionInactive),
            QueueSynchronizationOutcome.AlreadyRunning => output.WithError(
                PluggySynchronizationMessages.AlreadyRunning),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
