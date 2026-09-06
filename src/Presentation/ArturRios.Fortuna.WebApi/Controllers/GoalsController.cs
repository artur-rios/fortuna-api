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
[Route("api/goals")]
public sealed class GoalsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [GoalMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [GoalMessages.UpdatedSuccessfully] = StatusCodes.Status200OK,
            [GoalMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [GoalMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [GoalMessages.ProgressRetrievedSuccessfully] = StatusCodes.Status200OK,
            [GoalMessages.ListedSuccessfully] = StatusCodes.Status200OK,
            [GoalMessages.NotFound] = StatusCodes.Status404NotFound,
            [GoalMessages.ResourceNotFound] = StatusCodes.Status404NotFound,
            [GoalMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
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
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalCommandOutput?>>> Create(
        [FromBody] CreateGoalCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateGoalCommand,
            GoalCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalListOutput?>>> List(
        [FromQuery] bool includeDeleted = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<ListGoalsQuery, GoalListOutput>(
            new ListGoalsQuery { IncludeDeleted = includeDeleted });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalOutput?>>> Get(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<GetGoalByIdQuery, GoalOutput>(
            new GetGoalByIdQuery { Id = id, IncludeDeleted = includeDeleted });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateGoalCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            UpdateGoalCommand,
            GoalCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}/progress")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalProgressDetailOutput?>>> GetProgress(Guid id)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetGoalProgressQuery,
            GoalProgressDetailOutput>(new GetGoalProgressQuery { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GoalCommandOutput?>>> Delete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            DeleteGoalCommand,
            GoalCommandOutput>(new DeleteGoalCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
