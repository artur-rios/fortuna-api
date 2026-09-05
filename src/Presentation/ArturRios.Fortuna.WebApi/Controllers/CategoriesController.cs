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
[Route("api/categories")]
public sealed class CategoriesController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [CategoryMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [CategoryMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [CategoryMessages.ParentNotFound] = StatusCodes.Status404NotFound,
            [CategoryMessages.DuplicateSiblingName] = StatusCodes.Status409Conflict,
            [CategoryMessages.CycleDetected] = StatusCodes.Status400BadRequest,
            [CategoryMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [CategoryMessages.NameTooLong] = StatusCodes.Status400BadRequest,
            [CategoryMessages.ParentIdInvalid] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateCategoryCommandOutput?>>> Create(
        [FromBody] CreateCategoryCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateCategoryCommand,
            CreateCategoryCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
