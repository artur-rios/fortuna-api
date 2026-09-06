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
[Route("api/budgets")]
public sealed class BudgetsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [BudgetMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [BudgetMessages.UpdatedSuccessfully] = StatusCodes.Status200OK,
            [BudgetMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [BudgetMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [BudgetMessages.ListedSuccessfully] = StatusCodes.Status200OK,
            [BudgetMessages.NotFound] = StatusCodes.Status404NotFound,
            [BudgetMessages.CategoryNotFound] = StatusCodes.Status404NotFound,
            [BudgetMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [BudgetMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [BudgetMessages.AmountMustBePositive] = StatusCodes.Status400BadRequest,
            [BudgetMessages.CurrencyRequired] = StatusCodes.Status400BadRequest,
            [BudgetMessages.CurrencyInvalid] = StatusCodes.Status400BadRequest,
            [BudgetMessages.PeriodTypeInvalid] = StatusCodes.Status400BadRequest,
            [BudgetMessages.PeriodStartRequired] = StatusCodes.Status400BadRequest,
            [BudgetMessages.CategoriesRequired] = StatusCodes.Status400BadRequest,
            [BudgetMessages.CategoryIdInvalid] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetCommandOutput?>>> Create(
        [FromBody] CreateBudgetCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateBudgetCommand,
            BudgetCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetListOutput?>>> List(
        [FromQuery] bool includeDeleted = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            ListBudgetsQuery,
            BudgetListOutput>(new ListBudgetsQuery { IncludeDeleted = includeDeleted });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetOutput?>>> Get(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetBudgetByIdQuery,
            BudgetOutput>(new GetBudgetByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted
            });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateBudgetCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            UpdateBudgetCommand,
            BudgetCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetCommandOutput?>>> Delete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            DeleteBudgetCommand,
            BudgetCommandOutput>(new DeleteBudgetCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
