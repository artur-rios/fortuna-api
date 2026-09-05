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
[Route("api/transfers")]
public sealed class TransfersController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [TransferMessages.RecordedSuccessfully] = StatusCodes.Status201Created,
            [TransferMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [TransferMessages.OriginFinancialAccountNotFound] = StatusCodes.Status404NotFound,
            [TransferMessages.DestinationFinancialAccountNotFound] =
                StatusCodes.Status404NotFound,
            [TransferMessages.DestinationStatementNotFound] = StatusCodes.Status404NotFound,
            [TransferMessages.AccountsMustDiffer] = StatusCodes.Status400BadRequest,
            [TransferMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
            [TransferMessages.ConvertedAmountTooSmall] = StatusCodes.Status400BadRequest,
            [TransferMessages.StatementOpen] = StatusCodes.Status409Conflict,
            [TransferMessages.StatementAlreadySettled] = StatusCodes.Status409Conflict,
            [TransferMessages.OriginFinancialAccountIdRequired] =
                StatusCodes.Status400BadRequest,
            [TransferMessages.ExactlyOneDestinationRequired] =
                StatusCodes.Status400BadRequest,
            [TransferMessages.AmountPositive] = StatusCodes.Status400BadRequest,
            [TransferMessages.AmountPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [TransferMessages.OccurredOnRequired] = StatusCodes.Status400BadRequest,
            [TransferMessages.OccurredOnTooFarInFuture] = StatusCodes.Status400BadRequest,
            [TransferMessages.OwnerImmutable] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordTransferCommandOutput?>>> Record(
        [FromBody] RecordTransferCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RecordTransferCommand,
            RecordTransferCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
