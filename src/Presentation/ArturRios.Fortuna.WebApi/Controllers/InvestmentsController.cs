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
[Route("api/investments")]
public sealed class InvestmentsController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [InvestmentMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [InvestmentMessages.DuplicateInstrument] = StatusCodes.Status409Conflict,
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
            [InvestmentMessages.ValuedOnFuture] = StatusCodes.Status400BadRequest
        };

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
