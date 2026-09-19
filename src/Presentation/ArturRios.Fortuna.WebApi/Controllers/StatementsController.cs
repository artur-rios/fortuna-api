using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/statements")]
public sealed class StatementsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [CreditCardStatementMessages.NotFound] = StatusCodes.Status404NotFound,
            [CreditCardStatementMessages.FinancialAccountNotFound] = StatusCodes.Status404NotFound,
            [CreditCardStatementMessages.SettledStatementFrozen] = StatusCodes.Status409Conflict,
            [CreditCardStatementMessages.StatementOpen] = StatusCodes.Status409Conflict,
            [CreditCardStatementMessages.StatementAlreadySettled] = StatusCodes.Status409Conflict,
            [CreditCardStatementMessages.StatementIdRequired] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.FinancialAccountIdRequired] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.PaymentAmountPositive] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.PaymentAmountPrecisionInvalid] =
                StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.PaymentDateRequired] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreditCardStatementOutput?>>> GetById(Guid id)
    {
        return await QueryAsync<
            GetCreditCardStatementByIdQuery,
            CreditCardStatementOutput>(new GetCreditCardStatementByIdQuery { Id = id });
    }

    [HttpPost("{id:guid}/close")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CloseCreditCardStatementCommandOutput?>>> Close(Guid id)
    {
        return await SendAsync<
            CloseCreditCardStatementCommand,
            CloseCreditCardStatementCommandOutput>(new CloseCreditCardStatementCommand { Id = id });
    }

    [HttpPost("{id:guid}/settle")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<SettleCreditCardStatementCommandOutput?>>> Settle(
        Guid id,
        [FromBody] SettleCreditCardStatementCommand command)
    {
        command.Id = id;

        return await SendAsync<
            SettleCreditCardStatementCommand,
            SettleCreditCardStatementCommandOutput>(command);
    }
}
