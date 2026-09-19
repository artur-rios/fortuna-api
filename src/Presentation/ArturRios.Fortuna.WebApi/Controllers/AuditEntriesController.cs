using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/audit-entries")]
public sealed class AuditEntriesController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [AuditEntryMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [AuditEntryMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [AuditEntryMessages.EntityTypeTooLong] = StatusCodes.Status400BadRequest,
            [AuditEntryMessages.OperationTooLong] = StatusCodes.Status400BadRequest,
            [AuditEntryMessages.OutcomeInvalid] = StatusCodes.Status400BadRequest,
            [AuditEntryMessages.PeriodInvalid] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<AuditEntryOutput>>> List(
        [FromQuery] ListAuditEntriesQuery query)
    {
        return await QueryPageAsync<
            ListAuditEntriesQuery,
            AuditEntryOutput>(query);
    }
}
