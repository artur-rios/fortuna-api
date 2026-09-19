using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [UserErasureMessages.ConfirmationInvalid] = StatusCodes.Status400BadRequest,
            [UserErasureMessages.UserNotFound] = StatusCodes.Status404NotFound
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.SystemAdmin)]
    public async Task<ActionResult<DataOutput<EraseUserCommandOutput?>>> Erase(
        Guid id,
        [FromBody] EraseUserCommand command)
    {
        command.UserId = id;
        command.IsSelfService = false;

        return await SendAsync<
            EraseUserCommand,
            EraseUserCommandOutput>(command);
    }
}
