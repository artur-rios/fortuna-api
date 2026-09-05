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
[Route("api/recurring-transactions")]
public sealed class RecurringTransactionsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [RecurringTransactionMessages.RecordedSuccessfully] = StatusCodes.Status201Created,
            [RecurringTransactionMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [RecurringTransactionMessages.MaterializedSuccessfully] = StatusCodes.Status200OK,
            [RecurringTransactionMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [RecurringTransactionMessages.FinancialAccountNotFound] = StatusCodes.Status404NotFound,
            [RecurringTransactionMessages.CreditCardNotFound] = StatusCodes.Status404NotFound,
            [RecurringTransactionMessages.CategoryNotFound] = StatusCodes.Status404NotFound,
            [RecurringTransactionMessages.NotFound] = StatusCodes.Status404NotFound,
            [RecurringTransactionMessages.ExactlyOneTargetRequired] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.CategoryIdRequired] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.DirectionInvalid] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.AmountPositive] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.AmountPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.FrequencyInvalid] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.StartsOnRequired] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.DateRangeInvalid] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.DescriptionTooLong] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.CounterpartyTooLong] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.OwnerImmutable] = StatusCodes.Status400BadRequest,
            [RecurringTransactionMessages.IdRequired] = StatusCodes.Status400BadRequest
        };

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecurringTransactionOutput?>>> GetById(Guid id)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetRecurringTransactionByIdQuery,
            RecurringTransactionOutput>(new GetRecurringTransactionByIdQuery { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<DefineRecurringTransactionCommandOutput?>>> Define(
        [FromBody] DefineRecurringTransactionCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            DefineRecurringTransactionCommand,
            DefineRecurringTransactionCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("materialize")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<MaterializeRecurringTransactionsCommandOutput?>>> Materialize(
        [FromBody] MaterializeRecurringTransactionsCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            MaterializeRecurringTransactionsCommand,
            MaterializeRecurringTransactionsCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
