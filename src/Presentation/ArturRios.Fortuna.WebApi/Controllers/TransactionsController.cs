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
            [TransactionMessages.RecordedSuccessfully] = StatusCodes.Status201Created,
            [TransactionMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.FinancialAccountNotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.CreditCardNotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.CategoryNotFound] = StatusCodes.Status404NotFound,
            [TransactionMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [TransactionMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
            [TransactionMessages.ConvertedAmountTooSmall] = StatusCodes.Status400BadRequest,
            [TransactionMessages.AmountPositive] = StatusCodes.Status400BadRequest,
            [TransactionMessages.AmountPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.OccurredOnRequired] = StatusCodes.Status400BadRequest,
            [TransactionMessages.OccurredOnTooFarInFuture] = StatusCodes.Status400BadRequest,
            [TransactionMessages.DirectionInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.ExactlyOneTargetRequired] = StatusCodes.Status400BadRequest,
            [TransactionMessages.CategoryIdRequired] = StatusCodes.Status400BadRequest,
            [TransactionMessages.CurrencyInvalid] = StatusCodes.Status400BadRequest,
            [TransactionMessages.DescriptionTooLong] = StatusCodes.Status400BadRequest,
            [TransactionMessages.CounterpartyTooLong] = StatusCodes.Status400BadRequest,
            [TransactionMessages.TooManyTags] = StatusCodes.Status400BadRequest,
            [TransactionMessages.TagRequired] = StatusCodes.Status400BadRequest,
            [TransactionMessages.TagTooLong] = StatusCodes.Status400BadRequest,
            [TransactionMessages.OwnerImmutable] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordTransactionCommandOutput?>>> Record(
        [FromBody] RecordTransactionCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RecordTransactionCommand,
            RecordTransactionCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
