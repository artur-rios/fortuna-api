using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/projections")]
public sealed class ProjectionsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> CashFlowStatuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [CashFlowProjectionMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [CashFlowProjectionMessages.PartiallyConverted] = StatusCodes.Status200OK
        });

    private static readonly IReadOnlyDictionary<string, int> CommitmentStatuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [CommittedObligationMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [CommittedObligationMessages.PartiallyConverted] = StatusCodes.Status200OK
        });

    [HttpGet("cash-flow")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CashFlowProjectionOutput?>>> CashFlow(
        [FromQuery] ProjectCashFlowQuery query)
    {
        return await QueryAsync<
            ProjectCashFlowQuery,
            CashFlowProjectionOutput>(query, CashFlowStatuses);
    }

    [HttpGet("commitments")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CommittedObligationListOutput?>>> Commitments(
        [FromQuery] ListCommittedObligationsQuery query)
    {
        return await QueryAsync<
            ListCommittedObligationsQuery,
            CommittedObligationListOutput>(query, CommitmentStatuses);
    }
}
