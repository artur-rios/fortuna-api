using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using ArturRios.Fortuna.WebApi.Filters;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/investments")]
public sealed class InvestmentsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [InvestmentMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [InvestmentMessages.DuplicateInstrument] = StatusCodes.Status409Conflict,
            [InvestmentMessages.CurrencyImmutable] = StatusCodes.Status400BadRequest,
            [InvestmentMessages.RestoreRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [InvestmentMessages.HardDeleteRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [InvestmentMessages.HardDeleteHasLiveGoal] = StatusCodes.Status409Conflict,
            [InvestmentMessages.HardDeleteHasDependents] = StatusCodes.Status409Conflict,
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
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpGet]
    [AllowedQuery(
        "PageNumber", "PageSize", "Instrument", "Institution", "InvestmentType", "CurrencyCode",
        "DisplayCurrencyCode", "FigureDate", "IncludeDeleted", "SortBy", "Descending")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<InvestmentOutput>>> List(
        [FromQuery] ListInvestmentsQuery query)
    {
        return await QueryPageAsync<
            ListInvestmentsQuery,
            InvestmentOutput>(query);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InvestmentOutput?>>> GetById(
        Guid id,
        [FromQuery] string? displayCurrencyCode = null,
        [FromQuery] DateOnly? figureDate = null)
    {
        return await QueryAsync<
            GetInvestmentByIdQuery,
            InvestmentOutput>(new GetInvestmentByIdQuery
            {
                Id = id,
                DisplayCurrencyCode = displayCurrencyCode,
                FigureDate = figureDate
            });
    }

    [HttpGet("{id:guid}/valuations")]
    [AllowedQuery("PageNumber", "PageSize", "From", "To", "SortBy", "Descending")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<InvestmentValuationOutput>>> ListValuations(
        Guid id,
        [FromQuery] ListInvestmentValuationsQuery query)
    {
        query.InvestmentId = id;

        return await QueryPageAsync<
            ListInvestmentValuationsQuery,
            InvestmentValuationOutput>(query);
    }

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateInvestmentCommandOutput?>>> Create(
        [FromBody] CreateInvestmentCommand command)
    {
        return await SendAsync<
            CreateInvestmentCommand,
            CreateInvestmentCommandOutput>(command);
    }

    [HttpPut("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<UpdateInvestmentCommandOutput?>>> Update(
        Guid id,
        [FromBody] UpdateInvestmentCommand command)
    {
        command.Id = id;

        return await SendAsync<
            UpdateInvestmentCommand,
            UpdateInvestmentCommandOutput>(command);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InvestmentLifecycleCommandOutput?>>> Delete(Guid id)
    {
        return await SendAsync<
            DeleteInvestmentCommand,
            InvestmentLifecycleCommandOutput>(new DeleteInvestmentCommand { Id = id });
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InvestmentLifecycleCommandOutput?>>> Restore(Guid id)
    {
        return await SendAsync<
            RestoreInvestmentCommand,
            InvestmentLifecycleCommandOutput>(new RestoreInvestmentCommand { Id = id });
    }

    [HttpDelete("{id:guid}/hard")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InvestmentLifecycleCommandOutput?>>> HardDelete(Guid id)
    {
        return await SendAsync<
            HardDeleteInvestmentCommand,
            InvestmentLifecycleCommandOutput>(new HardDeleteInvestmentCommand { Id = id });
    }

    [HttpPost("{id:guid}/movements")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordInvestmentMovementCommandOutput?>>> RecordMovement(
        Guid id,
        [FromBody] RecordInvestmentMovementCommand command)
    {
        command.Id = id;

        return await SendAsync<
            RecordInvestmentMovementCommand,
            RecordInvestmentMovementCommandOutput>(command);
    }

    [HttpPost("{id:guid}/valuations")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordInvestmentValuationCommandOutput?>>> RecordValuation(
        Guid id,
        [FromBody] RecordInvestmentValuationCommand command)
    {
        command.Id = id;

        return await SendAsync<
            RecordInvestmentValuationCommand,
            RecordInvestmentValuationCommandOutput>(command);
    }
}
