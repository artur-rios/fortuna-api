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
[Route("api/exchange-rates")]
public sealed class ExchangeRatesController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [ExchangeRateSyncMessages.SourceNotConfigured] = StatusCodes.Status400BadRequest,
            [ManualExchangeRateMessages.RecordedSuccessfully] = StatusCodes.Status201Created,
            [ManualExchangeRateMessages.ReplacedSuccessfully] = StatusCodes.Status200OK,
            [ManualExchangeRateMessages.BaseCurrencyRequired] = StatusCodes.Status400BadRequest,
            [ManualExchangeRateMessages.BaseCurrencyInvalid] = StatusCodes.Status400BadRequest,
            [ManualExchangeRateMessages.QuoteCurrencyRequired] = StatusCodes.Status400BadRequest,
            [ManualExchangeRateMessages.QuoteCurrencyInvalid] = StatusCodes.Status400BadRequest,
            [ManualExchangeRateMessages.RateMustBePositive] = StatusCodes.Status400BadRequest,
            [ManualExchangeRateMessages.RatePrecisionInvalid] = StatusCodes.Status400BadRequest,
            [ManualExchangeRateMessages.RateDateRequired] = StatusCodes.Status400BadRequest,
            [ManualExchangeRateMessages.CurrenciesMustDiffer] = StatusCodes.Status400BadRequest,
            [ManualExchangeRateMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordManualExchangeRateCommandOutput?>>> RecordManual(
        [FromBody] RecordManualExchangeRateCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RecordManualExchangeRateCommand,
            RecordManualExchangeRateCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("sync")]
    public async Task<ActionResult<DataOutput<SynchronizeExchangeRatesCommandOutput?>>> Synchronize(
        [FromBody] SynchronizeExchangeRatesCommand? command)
    {
        command ??= new SynchronizeExchangeRatesCommand();
        command.CorrelationId = HttpContext.TraceIdentifier;
        var result = await commandMediator.ExecuteCommandAsync<
            SynchronizeExchangeRatesCommand,
            SynchronizeExchangeRatesCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
