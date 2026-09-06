using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/connections")]
public sealed class ConnectionsController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [ConnectionMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [ConnectionMessages.Duplicate] = StatusCodes.Status409Conflict,
            [ConnectionMessages.InvalidReference] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.SourceUnavailable] = StatusCodes.Status503ServiceUnavailable,
            [ConnectionMessages.SourceNotAvailable] = StatusCodes.Status404NotFound,
            [ConnectionMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [ConnectionMessages.DataSourceRequired] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.DataSourceInvalid] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.ExternalReferenceRequired] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.ExternalReferenceInvalid] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.BankCredentialRejected] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateConnectionCommandOutput?>>> Create(
        [FromBody] CreateConnectionCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateConnectionCommand,
            CreateConnectionCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
