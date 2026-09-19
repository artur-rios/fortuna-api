using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using ArturRios.Fortuna.WebApi.Filters;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/import-jobs")]
public sealed class ImportJobsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [ImportJobMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [ImportJobMessages.NotFound] = StatusCodes.Status404NotFound,
            [ImportJobMessages.RetryAccepted] = StatusCodes.Status202Accepted,
            [ImportJobMessages.RetryRequiresFailedJob] = StatusCodes.Status409Conflict,
            [ImportJobMessages.SourceFileNotRetained] = StatusCodes.Status409Conflict,
            [ImportJobMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.SourceTypeInvalid] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.StatusInvalid] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.SortByUnsupported] = StatusCodes.Status400BadRequest
        };

    [HttpPost("{id:guid}/retry")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RetryImportJobCommandOutput?>>> Retry(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RetryImportJobCommand,
            RetryImportJobCommandOutput>(new RetryImportJobCommand { Id = id });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ImportJobOutput?>>> GetById(Guid id)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetImportJobByIdQuery,
            ImportJobOutput>(new GetImportJobByIdQuery { Id = id });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet]
    [AllowedQuery("PageNumber", "PageSize", "SourceType", "Status", "SortBy", "Descending")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<ImportJobOutput>>> List(
        [FromQuery] ListImportJobsQuery query)
    {
        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListImportJobsQuery,
            ImportJobOutput>(query);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}/records")]
    [AllowedQuery("PageNumber", "PageSize")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<ImportedRecordOutput>>> Records(
        Guid id,
        [FromQuery] ListImportedRecordsQuery query)
    {
        query.ImportJobId = id;
        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListImportedRecordsQuery,
            ImportedRecordOutput>(query);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
