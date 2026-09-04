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
[Route("api/transactions")]
public sealed class TransactionsController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [TransactionMessages.CardChargeCreatedSuccessfully] = StatusCodes.Status201Created,
            [TransactionMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.CreditCardNotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.CreditCardIdRequired] = StatusCodes.Status400BadRequest,
            [TransactionMessages.AmountPositive] = StatusCodes.Status400BadRequest,
            [TransactionMessages.AmountPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.OccurredOnRequired] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordCardChargeCommandOutput?>>> RecordCardCharge(
        [FromBody] RecordCardChargeCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RecordCardChargeCommand,
            RecordCardChargeCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
