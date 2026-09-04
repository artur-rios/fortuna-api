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
[Route("api/credit-cards")]
public sealed class CreditCardsController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [CreditCardMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [CreditCardMessages.DuplicateName] = StatusCodes.Status409Conflict,
            [CreditCardMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [CreditCardMessages.NameRequired] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.NameTooLong] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.IssuerRequired] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.IssuerTooLong] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CurrencyRequired] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CurrencyInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CreditLimitPositive] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.CreditLimitPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.ClosingDayInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.DueDayInvalid] = StatusCodes.Status400BadRequest,
            [CreditCardMessages.LastFourDigitsInvalid] = StatusCodes.Status400BadRequest
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateCreditCardCommandOutput?>>> Create(
        [FromBody] CreateCreditCardCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateCreditCardCommand,
            CreateCreditCardCommandOutput>(command);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
