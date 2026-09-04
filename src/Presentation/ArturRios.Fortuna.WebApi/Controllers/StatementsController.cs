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
[Route("api/statements")]
public sealed class StatementsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [CreditCardStatementMessages.NotFound] = StatusCodes.Status404NotFound,
            [CreditCardStatementMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [CreditCardStatementMessages.SettledStatementFrozen] = StatusCodes.Status409Conflict
        };

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreditCardStatementOutput?>>> GetById(Guid id)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetCreditCardStatementByIdQuery,
            CreditCardStatementOutput>(new GetCreditCardStatementByIdQuery { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/close")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CloseCreditCardStatementCommandOutput?>>> Close(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CloseCreditCardStatementCommand,
            CloseCreditCardStatementCommandOutput>(new CloseCreditCardStatementCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
