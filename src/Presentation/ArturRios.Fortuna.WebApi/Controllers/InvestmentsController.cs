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
[Route("api/investments")]
public sealed class InvestmentsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly HashSet<string> ListQueryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PageNumber",
        "PageSize",
        "Instrument",
        "Institution",
        "InvestmentType",
        "CurrencyCode",
        "DisplayCurrencyCode",
        "FigureDate",
        "SortBy",
        "Descending"
    };

    private static readonly HashSet<string> ValuationQueryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PageNumber",
        "PageSize",
        "From",
        "To",
        "SortBy",
        "Descending"
    };

    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [InvestmentMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [InvestmentMessages.DuplicateInstrument] = StatusCodes.Status409Conflict,
            [InvestmentMessages.CurrencyImmutable] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.RestoreRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [InvestmentMessages.HardDeleteRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [InvestmentMessages.HardDeleteHasLiveGoal] = StatusCodes.Status409Conflict,
            [InvestmentMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [InvestmentMessages.InstrumentRequired] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.InstrumentTooLong] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.InstitutionTooLong] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.InvestmentTypeInvalid] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.CurrencyRequired] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.CurrencyInvalid] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.NotFound] = StatusCodes.Status404NotFound,
            [InvestmentMessages.FinancialAccountNotFound] = StatusCodes.Status404NotFound,
            [InvestmentMessages.InvestmentIdRequired] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.MovementTypeInvalid] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.MovementAmountPositive] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.MovementAmountPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.OccurredOnRequired] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.OccurredOnTooFarInFuture] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.FinancialAccountIdInvalid] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.FundingRequiresContribution] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
            [InvestmentMessages.ConvertedAmountTooSmall] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.ValuationValuePrecisionInvalid] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.ValuedOnRequired] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.ValuedOnFuture] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.DisplayCurrencyInvalid] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.SortByUnsupported] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.ValuationSortByUnsupported] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.ValuationPeriodInvalid] = StatusCodes.Status400BadRequest
        };

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<InvestmentOutput>>> List(
        [FromQuery] ListInvestmentsQuery query)
    {
        var unsupported = Request.Query.Keys.FirstOrDefault(key => !ListQueryFields.Contains(key));
        if (unsupported is not null)
        {
            return BadRequest(PaginatedOutput<InvestmentOutput>.New
                .WithError(InvestmentMessages.UnsupportedFilter(unsupported)));
        }

        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListInvestmentsQuery,
            InvestmentOutput>(query);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InvestmentOutput?>>> GetById(
        Guid id,
        [FromQuery] string? displayCurrencyCode = null,
        [FromQuery] DateOnly? figureDate = null)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetInvestmentByIdQuery,
            InvestmentOutput>(new GetInvestmentByIdQuery
            {
                Id = id,
                DisplayCurrencyCode = displayCurrencyCode,
                FigureDate = figureDate
            });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}/valuations")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<InvestmentValuationOutput>>> ListValuations(
        Guid id,
        [FromQuery] ListInvestmentValuationsQuery query)
    {
        var unsupported = Request.Query.Keys.FirstOrDefault(key => !ValuationQueryFields.Contains(key));
        if (unsupported is not null)
        {
            return BadRequest(PaginatedOutput<InvestmentValuationOutput>.New
                .WithError(InvestmentMessages.UnsupportedFilter(unsupported)));
        }

        query.InvestmentId = id;
        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListInvestmentValuationsQuery,
            InvestmentValuationOutput>(query);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateInvestmentCommandOutput?>>> Create(
        [FromBody] CreateInvestmentCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateInvestmentCommand,
            CreateInvestmentCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<UpdateInvestmentCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateInvestmentCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            UpdateInvestmentCommand,
            UpdateInvestmentCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InvestmentLifecycleCommandOutput?>>> Delete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            DeleteInvestmentCommand,
            InvestmentLifecycleCommandOutput>(new DeleteInvestmentCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InvestmentLifecycleCommandOutput?>>> Restore(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RestoreInvestmentCommand,
            InvestmentLifecycleCommandOutput>(new RestoreInvestmentCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}/hard")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InvestmentLifecycleCommandOutput?>>> HardDelete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            HardDeleteInvestmentCommand,
            InvestmentLifecycleCommandOutput>(new HardDeleteInvestmentCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/movements")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordInvestmentMovementCommandOutput?>>> RecordMovement(
        Guid id,
        [FromBody] RecordInvestmentMovementCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            RecordInvestmentMovementCommand,
            RecordInvestmentMovementCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/valuations")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordInvestmentValuationCommandOutput?>>> RecordValuation(
        Guid id,
        [FromBody] RecordInvestmentValuationCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            RecordInvestmentValuationCommand,
            RecordInvestmentValuationCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
