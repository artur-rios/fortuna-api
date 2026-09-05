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
[Route("api/categories")]
public sealed class CategoriesController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [CategoryMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [CategoryMessages.NotFound] = StatusCodes.Status404NotFound,
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

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CategoryTreeOutput?>>> GetTree(
        [FromQuery] bool includeDeleted = false,
        [FromQuery] bool includeUsageCounts = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetCategoryTreeQuery,
            CategoryTreeOutput>(new GetCategoryTreeQuery
            {
                IncludeDeleted = includeDeleted,
                IncludeUsageCounts = includeUsageCounts
            });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CategoryOutput?>>> GetById(
        Guid id,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] bool includeUsageCounts = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetCategoryByIdQuery,
            CategoryOutput>(new GetCategoryByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted,
                IncludeUsageCounts = includeUsageCounts
            });

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
