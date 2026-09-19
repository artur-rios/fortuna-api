using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/tags")]
public sealed class TagsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [TagMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [TagMessages.UpdatedSuccessfully] = StatusCodes.Status200OK,
            [TagMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [TagMessages.ListedSuccessfully] = StatusCodes.Status200OK,
            [TagMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [TagMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [TagMessages.NotFound] = StatusCodes.Status404NotFound,
            [TagMessages.DuplicateName] = StatusCodes.Status409Conflict,
            [TagMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [TagMessages.NameTooLong] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TagCommandOutput?>>> Create(
        [FromBody] CreateTagCommand command)
    {
        return await SendAsync<
            CreateTagCommand,
            TagCommandOutput>(command);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TagListOutput?>>> List(
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100)
    {
        return await QueryAsync<
            ListTagsQuery,
            TagListOutput>(new ListTagsQuery
            {
                IncludeDeleted = includeDeleted,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TagCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateTagCommand command)
    {
        command.Id = id;

        return await SendAsync<
            UpdateTagCommand,
            TagCommandOutput>(command);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TagCommandOutput?>>> Delete(Guid id)
    {
        return await SendAsync<
            DeleteTagCommand,
            TagCommandOutput>(new DeleteTagCommand { Id = id });
    }
}
