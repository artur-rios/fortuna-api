using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/exchange-rates")]
public sealed class ExchangeRatesController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [ExchangeRateSyncMessages.SourceNotConfigured] = StatusCodes.Status400BadRequest
        };

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
