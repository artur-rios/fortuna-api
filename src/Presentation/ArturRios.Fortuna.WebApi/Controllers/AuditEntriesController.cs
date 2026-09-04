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
[Route("api/audit-entries")]
public sealed class AuditEntriesController(QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [AuditEntryMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [AuditEntryMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [AuditEntryMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [AuditEntryMessages.EntityTypeTooLong] = StatusCodes.Status400BadRequest,
            [AuditEntryMessages.OperationTooLong] = StatusCodes.Status400BadRequest,
            [AuditEntryMessages.OutcomeInvalid] = StatusCodes.Status400BadRequest,
            [AuditEntryMessages.PeriodInvalid] = StatusCodes.Status400BadRequest
        };

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<AuditEntryOutput>>> List(
        [FromQuery] ListAuditEntriesQuery query)
    {
        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListAuditEntriesQuery,
            AuditEntryOutput>(query);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
