using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/import-jobs")]
public sealed class ImportJobsController(QueryMediator queryMediator) : Controller
{
    private static readonly HashSet<string> ListQueryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PageNumber", "PageSize", "SourceType", "Status", "SortBy", "Descending"
    };

    private static readonly HashSet<string> RecordQueryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PageNumber", "PageSize"
    };

    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [ImportJobMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [ImportJobMessages.NotFound] = StatusCodes.Status404NotFound,
            [ImportJobMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.SourceTypeInvalid] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.StatusInvalid] = StatusCodes.Status400BadRequest,
            [ImportJobMessages.SortByUnsupported] = StatusCodes.Status400BadRequest
        };

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
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<ImportJobOutput>>> List(
        [FromQuery] ListImportJobsQuery query)
    {
        var unsupported = Request.Query.Keys.FirstOrDefault(key => !ListQueryFields.Contains(key));
        if (unsupported is not null)
        {
            return BadRequest(PaginatedOutput<ImportJobOutput>.New.WithError(
                ImportJobMessages.UnsupportedFilter(unsupported)));
        }

        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListImportJobsQuery,
            ImportJobOutput>(query);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}/records")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<ImportedRecordOutput>>> Records(
        Guid id,
        [FromQuery] ListImportedRecordsQuery query)
    {
        var unsupported = Request.Query.Keys.FirstOrDefault(key => !RecordQueryFields.Contains(key));
        if (unsupported is not null)
        {
            return BadRequest(PaginatedOutput<ImportedRecordOutput>.New.WithError(
                ImportJobMessages.UnsupportedFilter(unsupported)));
        }

        query.ImportJobId = id;
        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListImportedRecordsQuery,
            ImportedRecordOutput>(query);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
