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
[Route("api/goals")]
public sealed class GoalsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [GoalMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [GoalMessages.UpdatedSuccessfully] = StatusCodes.Status200OK,
            [GoalMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [GoalMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [GoalMessages.ProgressRetrievedSuccessfully] = StatusCodes.Status200OK,
            [GoalMessages.ListedSuccessfully] = StatusCodes.Status200OK,
            [GoalMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [GoalMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [GoalMessages.NotFound] = StatusCodes.Status404NotFound,
            [GoalMessages.ResourceNotFound] = StatusCodes.Status404NotFound,
            [GoalMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [GoalMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [GoalMessages.NameTooLong] = StatusCodes.Status400BadRequest,
            [GoalMessages.TargetAmountMustBePositive] = StatusCodes.Status400BadRequest,
            [GoalMessages.CurrencyRequired] = StatusCodes.Status400BadRequest,
            [GoalMessages.CurrencyInvalid] = StatusCodes.Status400BadRequest,
            [GoalMessages.TargetDateRequired] = StatusCodes.Status400BadRequest,
            [GoalMessages.TargetDateMustBeFuture] = StatusCodes.Status400BadRequest,
            [GoalMessages.ResourcesRequired] = StatusCodes.Status400BadRequest,
            [GoalMessages.ResourceIdInvalid] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalCommandOutput?>>> Create(
        [FromBody] CreateGoalCommand command)
    {
        return await SendAsync<
            CreateGoalCommand,
            GoalCommandOutput>(command);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalListOutput?>>> List(
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100)
    {
        return await QueryAsync<ListGoalsQuery, GoalListOutput>(
            new ListGoalsQuery
            {
                IncludeDeleted = includeDeleted,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalOutput?>>> Get(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        return await QueryAsync<GetGoalByIdQuery, GoalOutput>(
            new GetGoalByIdQuery { Id = id, IncludeDeleted = includeDeleted });
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateGoalCommand command)
    {
        command.Id = id;

        return await SendAsync<
            UpdateGoalCommand,
            GoalCommandOutput>(command);
    }

    [HttpGet("{id:guid}/progress")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalProgressDetailOutput?>>> GetProgress(Guid id)
    {
        return await QueryAsync<
            GetGoalProgressQuery,
            GoalProgressDetailOutput>(new GetGoalProgressQuery { Id = id });
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalCommandOutput?>>> Delete(Guid id)
    {
        return await SendAsync<
            DeleteGoalCommand,
            GoalCommandOutput>(new DeleteGoalCommand { Id = id });
    }
}
