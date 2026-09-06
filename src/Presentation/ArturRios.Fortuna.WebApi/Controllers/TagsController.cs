using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/tags")]
public sealed class TagsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [TagMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [TagMessages.UpdatedSuccessfully] = StatusCodes.Status200OK,
            [TagMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [TagMessages.ListedSuccessfully] = StatusCodes.Status200OK,
            [TagMessages.NotFound] = StatusCodes.Status404NotFound,
            [TagMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [TagMessages.DuplicateName] = StatusCodes.Status409Conflict,
            [TagMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [TagMessages.NameTooLong] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TagCommandOutput?>>> Create(
        [FromBody] CreateTagCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateTagCommand,
            TagCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TagListOutput?>>> List(
        [FromQuery] bool includeDeleted = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            ListTagsQuery,
            TagListOutput>(new ListTagsQuery { IncludeDeleted = includeDeleted });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TagCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateTagCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            UpdateTagCommand,
            TagCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TagCommandOutput?>>> Delete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            DeleteTagCommand,
            TagCommandOutput>(new DeleteTagCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
