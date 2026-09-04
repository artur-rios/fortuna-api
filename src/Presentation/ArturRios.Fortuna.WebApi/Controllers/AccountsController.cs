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
[Route("api/accounts")]
public sealed class AccountsController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [FinancialAccountMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [FinancialAccountMessages.DuplicateName] = StatusCodes.Status409Conflict,
            [FinancialAccountMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [FinancialAccountMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.NameTooLong] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.InstitutionTooLong] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.AccountTypeInvalid] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.CurrencyRequired] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.CurrencyInvalid] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [FinancialAccountMessages.OpeningBalancePrecisionInvalid] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateFinancialAccountCommandOutput?>>> Create(
        [FromBody] CreateFinancialAccountCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateFinancialAccountCommand,
            CreateFinancialAccountCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
