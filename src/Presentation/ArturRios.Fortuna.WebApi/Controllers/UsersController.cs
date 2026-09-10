using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [UserErasureMessages.ConfirmationInvalid] = StatusCodes.Status400BadRequest,
            [UserErasureMessages.UserNotFound] = StatusCodes.Status404NotFound
        };

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<DataOutput<EraseUserCommandOutput?>>> Erase(
        Guid id,
        [FromBody] EraseUserCommand command)
    {
        command.UserId = id;
        command.IsSelfService = false;
        var result = await commandMediator.ExecuteCommandAsync<
            EraseUserCommand,
            EraseUserCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
