using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using ArturRios.Fortuna.WebApi.Filters;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/import-jobs")]
public sealed class ImportJobsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [ImportJobMessages.NotFound] = StatusCodes.Status404NotFound,
            [ImportJobMessages.RetryAccepted] = StatusCodes.Status202Accepted,
            [ImportJobMessages.RetryRequiresFailedJob] = StatusCodes.Status409Conflict,
            [ImportJobMessages.SourceFileNotRetained] = StatusCodes.Status409Conflict,
            [ImportJobMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.SourceTypeInvalid] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.StatusInvalid] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.SortByUnsupported] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost("{id:guid}/retry")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RetryImportJobCommandOutput?>>> Retry(Guid id)
    {
        return await SendAsync<
            RetryImportJobCommand,
            RetryImportJobCommandOutput>(new RetryImportJobCommand { Id = id });
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ImportJobOutput?>>> GetById(Guid id)
    {
        return await QueryAsync<
            GetImportJobByIdQuery,
            ImportJobOutput>(new GetImportJobByIdQuery { Id = id });
    }

    [HttpGet]
    [AllowedQuery("PageNumber", "PageSize", "SourceType", "Status", "SortBy", "Descending")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<ImportJobOutput>>> List(
        [FromQuery] ListImportJobsQuery query)
    {
        return await QueryPageAsync<
            ListImportJobsQuery,
            ImportJobOutput>(query);
    }

    [HttpGet("{id:guid}/records")]
    [AllowedQuery("PageNumber", "PageSize")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<ImportedRecordOutput>>> Records(
        Guid id,
        [FromQuery] ListImportedRecordsQuery query)
    {
        query.ImportJobId = id;

        return await QueryPageAsync<
            ListImportedRecordsQuery,
            ImportedRecordOutput>(query);
    }
}
