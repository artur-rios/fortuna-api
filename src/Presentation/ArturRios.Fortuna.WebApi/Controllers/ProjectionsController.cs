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
[Route("api/projections")]
public sealed class ProjectionsController(QueryMediator queryMediator) : Controller
{
    [HttpGet("cash-flow")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CashFlowProjectionOutput?>>> CashFlow(
        [FromQuery] ProjectCashFlowQuery query)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            ProjectCashFlowQuery,
            CashFlowProjectionOutput>(query);
        if (result.Errors?.Count > 0 &&
            !result.Errors.Contains(CashFlowProjectionMessages.ProfileNotFound))
        {
            return BadRequest(result);
        }

        return ResponseResolver.Resolve(result, statusMap: new Dictionary<string, int>
        {
            [CashFlowProjectionMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [CashFlowProjectionMessages.ProfileNotFound] = StatusCodes.Status404NotFound
        });
    }

    [HttpGet("commitments")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CommittedObligationListOutput?>>> Commitments(
        [FromQuery] ListCommittedObligationsQuery query)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            ListCommittedObligationsQuery,
            CommittedObligationListOutput>(query);
        if (result.Errors?.Count > 0 &&
            !result.Errors.Contains(CommittedObligationMessages.ProfileNotFound))
        {
            return BadRequest(result);
        }

        return ResponseResolver.Resolve(result, statusMap: new Dictionary<string, int>
        {
            [CommittedObligationMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [CommittedObligationMessages.PartiallyConverted] = StatusCodes.Status200OK,
            [CommittedObligationMessages.ProfileNotFound] = StatusCodes.Status404NotFound
        });
    }
}
