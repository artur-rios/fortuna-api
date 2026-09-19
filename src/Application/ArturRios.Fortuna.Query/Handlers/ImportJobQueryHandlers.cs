using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetImportJobByIdQueryHandler(
    ICurrentProfileResolver profileResolver,
    IImportJobReader jobs)
    : IQueryHandlerAsync<GetImportJobByIdQuery, ImportJobOutput>
{
    public async Task<DataOutput<ImportJobOutput?>> HandleAsync(GetImportJobByIdQuery query)
    {
        var output = DataOutput<ImportJobOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(ImportJobMessages.ProfileNotFound);
        }

        var job = await jobs.FindByIdAsync(profile.Id, query.Id, CancellationToken.None);

        return job is null
            ? output.WithError(ImportJobMessages.NotFound)
            : output.WithData(ImportJobProjection.Project(job)).WithMessage(
                ImportJobMessages.RetrievedSuccessfully);
    }
}

public sealed class ListImportJobsQueryHandler(
    ICurrentProfileResolver profileResolver,
    IImportJobReader jobs,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListImportJobsQuery, ImportJobOutput>
{
    public async Task<PaginatedOutput<ImportJobOutput>> HandleAsync(ListImportJobsQuery query)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return PaginatedOutput<ImportJobOutput>.New.WithError(
                ImportJobMessages.ProfileNotFound);
        }

        var filtered = jobs.Query().Where(item => item.User.PublicId == profile.Id);
        if (query.SourceType.HasValue)
        {
            filtered = filtered.Where(item => item.SourceType == query.SourceType.Value);
        }

        if (query.Status.HasValue)
        {
            filtered = filtered.Where(item => item.Status == query.Status.Value);
        }

        var projected = Order(filtered, query.SortBy.Trim(), query.Descending)
            .Select(ImportJobProjection.Expression);
        var result = await projected.PaginateAsync(
            query.PageNumber,
            Math.Min(query.PageSize, paginationOptions.MaximumPageSize),
            orderBy: null,
            cancellationToken: CancellationToken.None);

        return result.WithMessage(ImportJobMessages.ListedSuccessfully);
    }

    private static IOrderedQueryable<ImportJob> Order(
        IQueryable<ImportJob> jobs,
        string sortBy,
        bool descending) => sortBy.ToLowerInvariant() switch
        {
            "sourcetype" => jobs.SortBy(item => item.SourceType, item => item.PublicId, descending),
            "status" => jobs.SortBy(item => item.Status, item => item.PublicId, descending),
            "updatedat" => jobs.SortBy(item => item.UpdatedAt, item => item.PublicId, descending),
            _ => jobs.SortBy(item => item.CreatedAt, item => item.PublicId, descending)
        };
}

public sealed class ListImportedRecordsQueryHandler(
    ICurrentProfileResolver profileResolver,
    IImportJobReader jobs,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListImportedRecordsQuery, ImportedRecordOutput>
{
    public async Task<PaginatedOutput<ImportedRecordOutput>> HandleAsync(
        ListImportedRecordsQuery query)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return PaginatedOutput<ImportedRecordOutput>.New.WithError(
                ImportJobMessages.ProfileNotFound);
        }

        var owned = await jobs.IsOwnedAsync(
            profile.Id,
            query.ImportJobId,
            CancellationToken.None);
        if (!owned)
        {
            return PaginatedOutput<ImportedRecordOutput>.New.WithError(
                ImportJobMessages.NotFound);
        }

        var projected = jobs.Records()
            .Where(record => record.ImportJob.PublicId == query.ImportJobId)
            .OrderBy(record => record.Id)
            .Select(record => new ImportedRecordOutput
            {
                RawPayload = record.RawPayload,
                ExternalId = record.ExternalId,
                Outcome = record.Outcome,
                RejectionReason = record.RejectionReason,
                Amount = record.Amount,
                OccurredOn = record.OccurredOn,
                TransactionId = record.Transaction == null
                    ? null
                    : record.Transaction.PublicId,
                HasLiveTransaction = record.Transaction != null &&
                    !record.Transaction.IsDeleted
            });
        var result = await projected.PaginateAsync(
            query.PageNumber,
            Math.Min(query.PageSize, paginationOptions.MaximumPageSize),
            orderBy: null,
            cancellationToken: CancellationToken.None);

        return result.WithMessage(ImportJobMessages.RecordsListedSuccessfully);
    }
}

internal static class ImportJobProjection
{
    public static readonly System.Linq.Expressions.Expression<Func<ImportJob, ImportJobOutput>>
        Expression = job => new ImportJobOutput
        {
            Id = job.PublicId,
            ConnectionId = job.Connection == null ? null : job.Connection.PublicId,
            SourceType = job.SourceType,
            Status = job.Status,
            PeriodStart = job.PeriodStart,
            PeriodEnd = job.PeriodEnd,
            ProcessedCount = job.ImportedCount + job.DuplicateCount + job.RejectedCount,
            ImportedCount = job.ImportedCount,
            DuplicateCount = job.DuplicateCount,
            RejectedCount = job.RejectedCount,
            FailureReason = job.FailureReason,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt
        };

    public static ImportJobOutput Project(ImportJobReadSnapshot job) => new()
    {
        Id = job.Id,
        ConnectionId = job.ConnectionId,
        SourceType = job.SourceType,
        Status = job.Status,
        PeriodStart = job.PeriodStart,
        PeriodEnd = job.PeriodEnd,
        ProcessedCount = job.ImportedCount + job.DuplicateCount + job.RejectedCount,
        ImportedCount = job.ImportedCount,
        DuplicateCount = job.DuplicateCount,
        RejectedCount = job.RejectedCount,
        FailureReason = job.FailureReason,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt
    };
}

