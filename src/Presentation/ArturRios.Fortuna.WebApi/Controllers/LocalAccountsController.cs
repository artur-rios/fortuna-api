using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/local-accounts")]
public sealed class LocalAccountsController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [LocalAccountMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [LocalAccountMessages.Disabled] = StatusCodes.Status404NotFound,
            [LocalAccountMessages.AlreadyExists] = StatusCodes.Status409Conflict,
            [LocalAccountMessages.CredentialStoreUnavailable] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<DataOutput<CreateLocalAccountCommandOutput?>>> Create(
        [FromBody] CreateLocalAccountCommand command)
    {
        var result = await commandMediator
            .ExecuteCommandAsync<CreateLocalAccountCommand, CreateLocalAccountCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
