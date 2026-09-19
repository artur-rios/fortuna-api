using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/exchange-rates")]
public sealed class ExchangeRatesController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [ExchangeRateSyncMessages.Accepted] = StatusCodes.Status200OK,
            [ExchangeRateSyncMessages.AlreadyQueued] = StatusCodes.Status200OK,
            [ExchangeRateSyncMessages.SourceNotConfigured] = StatusCodes.Status503ServiceUnavailable,
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
            [ManualExchangeRateMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [FigureConversionMessages.DisplayCurrencyInvalid] = StatusCodes.Status400BadRequest,
            [FigureConversionMessages.FigureDateRequired] = StatusCodes.Status400BadRequest,
            [FigureConversionMessages.AmountsRequired] = StatusCodes.Status400BadRequest,
            [FigureConversionMessages.AmountRequired] = StatusCodes.Status400BadRequest,
            [FigureConversionMessages.TooManyAmounts] = StatusCodes.Status400BadRequest,
            [FigureConversionMessages.AmountCurrencyRequired] = StatusCodes.Status400BadRequest,
            [FigureConversionMessages.AmountCurrencyInvalid] = StatusCodes.Status400BadRequest,
            [FigureConversionMessages.AmountPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [FigureConversionMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost("convert")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ConvertFigureQueryOutput?>>> Convert(
        [FromBody] ConvertFigureQuery query)
    {
        return await QueryAsync<ConvertFigureQuery, ConvertFigureQueryOutput>(query);
    }

    [HttpPost]
    [InstallationAdministratorRequirement]
    public async Task<ActionResult<DataOutput<RecordManualExchangeRateCommandOutput?>>> RecordManual(
        [FromBody] RecordManualExchangeRateCommand command)
    {
        return await SendAsync<
            RecordManualExchangeRateCommand,
            RecordManualExchangeRateCommandOutput>(command);
    }

    [HttpPost("sync")]
    [InstallationAdministratorRequirement]
    public async Task<ActionResult<DataOutput<SynchronizeExchangeRatesCommandOutput?>>> Synchronize(
        [FromBody] SynchronizeExchangeRatesCommand? command)
    {
        command ??= new SynchronizeExchangeRatesCommand();
        command.CorrelationId = HttpContext.TraceIdentifier;

        return await SendAsync<
            SynchronizeExchangeRatesCommand,
            SynchronizeExchangeRatesCommandOutput>(command);
    }
}
