using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/connections")]
public sealed class ConnectionSynchronizationController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [PluggySynchronizationMessages.Accepted] = StatusCodes.Status202Accepted,
            [PluggySynchronizationMessages.ConnectionNotFound] = StatusCodes.Status404NotFound,
            [PluggySynchronizationMessages.ConnectionInactive] = StatusCodes.Status409Conflict,
            [PluggySynchronizationMessages.AlreadyRunning] = StatusCodes.Status409Conflict,
            [PluggySynchronizationMessages.PeriodInvalid] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.RequiresReauthentication] = StatusCodes.Status409Conflict,
            [ConnectionMessages.Revoked] = StatusCodes.Status409Conflict
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost("{id:guid}/sync")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<SynchronizeConnectionCommandOutput?>>> Synchronize(
        Guid id,
        [FromBody] SynchronizeConnectionCommand command)
    {
        command.Id = id;
        command.CorrelationId = HttpContext.TraceIdentifier;

        return await SendAsync<
            SynchronizeConnectionCommand,
            SynchronizeConnectionCommandOutput>(command);
    }
}
