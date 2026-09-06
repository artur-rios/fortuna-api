using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RetryImportJobCommandHandler(
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IImportJobRetryStore importJobs,
    IBackgroundJobQueue queue,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RetryImportJobCommand, RetryImportJobCommandOutput>
{
    public async Task<DataOutput<RetryImportJobCommandOutput?>> HandleAsync(
        RetryImportJobCommand command)
    {
        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return DataOutput<RetryImportJobCommandOutput?>.New.WithError(
                ImportJobMessages.ProfileNotFound);
        }

        var result = await importJobs.RetryAsync(
            profile.Id,
            command.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);
        if (result.Outcome == RetryImportJobOutcome.Succeeded)
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

    private static DataOutput<RetryImportJobCommandOutput?> Resolve(RetryImportJobResult result)
    {
        var output = DataOutput<RetryImportJobCommandOutput?>.New;
        if (result.Job is not null)
        {
            output = output.WithData(new RetryImportJobCommandOutput
            {
                Id = result.Job.Id,
                SourceType = result.Job.SourceType,
                Status = result.Job.Status,
                PeriodStart = result.Job.PeriodStart,
                PeriodEnd = result.Job.PeriodEnd,
                ImportedCount = result.Job.ImportedCount,
                DuplicateCount = result.Job.DuplicateCount,
                RejectedCount = result.Job.RejectedCount,
                CreatedAt = result.Job.CreatedAt,
                UpdatedAt = result.Job.UpdatedAt
            });
        }

        return result.Outcome switch
        {
            RetryImportJobOutcome.Succeeded => output.WithMessage(ImportJobMessages.RetryAccepted),
            RetryImportJobOutcome.NotFound => output.WithError(ImportJobMessages.NotFound),
            RetryImportJobOutcome.NotFailed => output.WithError(
                ImportJobMessages.RetryRequiresFailedJob),
            RetryImportJobOutcome.SourceFileNotRetained => output.WithError(
                ImportJobMessages.SourceFileNotRetained),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
