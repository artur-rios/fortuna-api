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
[Route("api/budgets")]
public sealed class BudgetsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [BudgetMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [BudgetMessages.UpdatedSuccessfully] = StatusCodes.Status200OK,
            [BudgetMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [BudgetMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [BudgetMessages.ListedSuccessfully] = StatusCodes.Status200OK,
            [BudgetMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [BudgetMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [BudgetMessages.ConsumptionRetrievedSuccessfully] = StatusCodes.Status200OK,
            [BudgetMessages.PeriodPrecedesBudget] = StatusCodes.Status200OK,
            [BudgetMessages.NotFound] = StatusCodes.Status404NotFound,
            [BudgetMessages.CategoryNotFound] = StatusCodes.Status404NotFound,
            [BudgetMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [BudgetMessages.AmountMustBePositive] = StatusCodes.Status400BadRequest,
            [BudgetMessages.CurrencyRequired] = StatusCodes.Status400BadRequest,
            [BudgetMessages.CurrencyInvalid] = StatusCodes.Status400BadRequest,
            [BudgetMessages.PeriodTypeInvalid] = StatusCodes.Status400BadRequest,
            [BudgetMessages.PeriodStartRequired] = StatusCodes.Status400BadRequest,
            [BudgetMessages.CategoriesRequired] = StatusCodes.Status400BadRequest,
            [BudgetMessages.CategoryIdInvalid] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetCommandOutput?>>> Create(
        [FromBody] CreateBudgetCommand command)
    {
        return await SendAsync<
            CreateBudgetCommand,
            BudgetCommandOutput>(command);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetListOutput?>>> List(
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100)
    {
        return await QueryAsync<
            ListBudgetsQuery,
            BudgetListOutput>(new ListBudgetsQuery
            {
                IncludeDeleted = includeDeleted,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetOutput?>>> Get(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        return await QueryAsync<
            GetBudgetByIdQuery,
            BudgetOutput>(new GetBudgetByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted
            });
    }

    [HttpGet("{id:guid}/consumption")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetConsumptionDetailOutput?>>> GetConsumption(
        Guid id,
        [FromQuery] DateOnly? periodStart = null)
    {
        return await QueryAsync<
            GetBudgetConsumptionQuery,
            BudgetConsumptionDetailOutput>(new GetBudgetConsumptionQuery
            {
                Id = id,
                PeriodStart = periodStart
            });
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateBudgetCommand command)
    {
        command.Id = id;

        return await SendAsync<
            UpdateBudgetCommand,
            BudgetCommandOutput>(command);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<BudgetCommandOutput?>>> Delete(Guid id)
    {
        return await SendAsync<
            DeleteBudgetCommand,
            BudgetCommandOutput>(new DeleteBudgetCommand { Id = id });
    }
}
