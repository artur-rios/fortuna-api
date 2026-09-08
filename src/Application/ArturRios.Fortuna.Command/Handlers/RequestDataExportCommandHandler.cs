using System.Globalization;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RequestDataExportCommandHandler(
    IValidator<RequestDataExportCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    DataExportBuilder builder,
    IDataExportRenderer renderer,
    IDataExportStore exports,
    IBackgroundJobQueue queue,
    DataExportOptions options,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RequestDataExportCommand, RequestDataExportCommandOutput>
{
    public async Task<DataOutput<RequestDataExportCommandOutput?>> HandleAsync(
        RequestDataExportCommand command)
    {
        var output = DataOutput<RequestDataExportCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(DataExportMessages.ProfileNotFound);
        }

        var format = ParseFormat(command.Format);
        var locale = string.IsNullOrWhiteSpace(command.Locale)
            ? options.DefaultLocale
            : CultureInfo.GetCultureInfo(command.Locale.Trim()).Name;
        var specification = new DataExportSpecification(
            command.RecordSet.Trim(),
            command.Columns.Select(column => column.Trim()).ToArray(),
            command.Filters.Select(filter => new DataExportFilter(
                filter.Field.Trim(), filter.Operator.Trim(), filter.Value)).ToArray(),
            command.Sorts.Select(sort => new DataExportSort(
                sort.Field.Trim(), sort.Descending)).ToArray(),
            command.DisplayCurrencyCode?.Trim().ToUpperInvariant(),
            format,
            locale);
        var requestedRows = options.SynchronousThresholdRows == int.MaxValue
            ? int.MaxValue
            : options.SynchronousThresholdRows + 1;
        var built = await builder.BuildAsync(
            profile.Id,
            specification,
            requestedRows,
            CancellationToken.None);
        if (built.Error is not null)
        {
            return output.WithError(built.Error);
        }

        var now = timeProvider.GetUtcNow();
        var fileName = FileName(specification.RecordSet, format, now);
        if (built.TotalCount > options.SynchronousThresholdRows)
        {
            var queued = await exports.QueueAsync(new QueueDataExportRequest(
                profile.Id,
                specification,
                fileName,
                command.CorrelationId,
                now,
                now.Add(options.Retention)),
                CancellationToken.None);
            await queue.EnqueueAsync(queued.BackgroundJobId, CancellationToken.None);
            return output
                .WithData(new RequestDataExportCommandOutput
                {
                    Delivery = DataExportDelivery.Queued,
                    ExportId = queued.ExportId,
                    JobId = queued.BackgroundJobId,
                    Format = format,
                    RowCount = built.TotalCount,
                    FileName = fileName
                })
                .WithMessage(DataExportMessages.Accepted);
        }

        var rendered = renderer.Render(built.Document!, format);
        return output
            .WithData(new RequestDataExportCommandOutput
            {
                Delivery = DataExportDelivery.Direct,
                Format = format,
                RowCount = built.TotalCount,
                FileName = fileName,
                ContentType = rendered.ContentType,
                Content = rendered.Content
            })
            .WithMessage(DataExportMessages.CreatedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private static DataExportFormat ParseFormat(string format) =>
        format.Trim().ToLowerInvariant() switch
        {
            "csv" => DataExportFormat.Csv,
            "xlsx" or "excel" => DataExportFormat.Excel,
            "pdf" => DataExportFormat.Pdf,
            _ => throw new InvalidOperationException("The validated export format was invalid.")
        };

    private static string FileName(
        string recordSet,
        DataExportFormat format,
        DateTimeOffset createdAt)
    {
        var extension = format switch
        {
            DataExportFormat.Csv => "csv",
            DataExportFormat.Excel => "xlsx",
            DataExportFormat.Pdf => "pdf",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        return $"fortuna-{recordSet.ToLowerInvariant()}-{createdAt:yyyyMMddHHmmss}.{extension}";
    }
}
