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

public sealed class ImportExcelWorkbookCommandHandler(
    IValidator<ImportExcelWorkbookCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IExcelWorkbookParser parser,
    IExcelImportStore imports,
    IBackgroundJobQueue queue,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<ImportExcelWorkbookCommand, ImportExcelWorkbookCommandOutput>
{
    public async Task<DataOutput<ImportExcelWorkbookCommandOutput?>> HandleAsync(
        ImportExcelWorkbookCommand command)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return DataOutput<ImportExcelWorkbookCommandOutput?>.New.WithErrors(
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var workbook = parser.Validate(command.Content, command.Mapping);
        if (!workbook.IsValid)
        {
            return DataOutput<ImportExcelWorkbookCommandOutput?>.New.WithError(
                workbook.Error ?? ExcelImportMessages.WorkbookInvalid);
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return DataOutput<ImportExcelWorkbookCommandOutput?>.New.WithError(
                ExcelImportMessages.ProfileNotFound);
        }

        var result = await imports.QueueAsync(new ExcelImportRequest(
            profile.Id,
            command.TargetId,
            command.TargetType,
            command.FileName,
            command.Content,
            command.Mapping,
            command.CreateMissingCategories,
            command.CorrelationId,
            timeProvider.GetUtcNow()), CancellationToken.None);
        if (result.Outcome == QueueExcelImportOutcome.Succeeded)
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

    private static DataOutput<ImportExcelWorkbookCommandOutput?> Resolve(
        QueueExcelImportResult result)
    {
        var output = DataOutput<ImportExcelWorkbookCommandOutput?>.New;
        if (result.Job is not null)
        {
            output = output.WithData(new ImportExcelWorkbookCommandOutput
            {
                ImportJobId = result.Job.Id,
                Status = result.Job.Status
            });
        }

        return result.Outcome switch
        {
            QueueExcelImportOutcome.Succeeded => output.WithMessage(ExcelImportMessages.Accepted),
            QueueExcelImportOutcome.TargetNotFound => output.WithError(
                ExcelImportMessages.TargetNotFound),
            QueueExcelImportOutcome.TargetDeleted => output.WithError(
                ExcelImportMessages.TargetDeleted),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
