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
[Route("api/installment-plans")]
public sealed class InstallmentPlansController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [InstallmentPlanMessages.RecordedSuccessfully] = StatusCodes.Status201Created,
            [InstallmentPlanMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [InstallmentPlanMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [InstallmentPlanMessages.RestoredSuccessfully] = StatusCodes.Status200OK,
            [InstallmentPlanMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [InstallmentPlanMessages.NotFound] = StatusCodes.Status404NotFound,
            [InstallmentPlanMessages.CreditCardNotFound] = StatusCodes.Status404NotFound,
            [InstallmentPlanMessages.CategoryNotFound] = StatusCodes.Status404NotFound,
            [InstallmentPlanMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
            [InstallmentPlanMessages.AmountTooSmall] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.SettledStatementFrozen] = StatusCodes.Status409Conflict,
            [InstallmentPlanMessages.RestoreRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [InstallmentPlanMessages.IdRequired] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.CreditCardIdRequired] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.CategoryIdRequired] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.TotalAmountPositive] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.TotalAmountPrecisionInvalid] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.InstallmentCountMinimum] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.PurchasedOnRequired] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.PurchasedOnTooFarInFuture] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.CurrencyCodeInvalid] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.CounterpartyTooLong] = StatusCodes.Status400BadRequest,
            [InstallmentPlanMessages.OwnerImmutable] = StatusCodes.Status400BadRequest
        };

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InstallmentPlanOutput?>>> GetById(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetInstallmentPlanByIdQuery,
            InstallmentPlanOutput>(new GetInstallmentPlanByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted
            });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordInstallmentPlanCommandOutput?>>> Record(
        [FromBody] RecordInstallmentPlanCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RecordInstallmentPlanCommand,
            RecordInstallmentPlanCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InstallmentPlanLifecycleCommandOutput?>>> Delete(
        Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            DeleteInstallmentPlanCommand,
            InstallmentPlanLifecycleCommandOutput>(new DeleteInstallmentPlanCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InstallmentPlanLifecycleCommandOutput?>>> Restore(
        Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RestoreInstallmentPlanCommand,
            InstallmentPlanLifecycleCommandOutput>(new RestoreInstallmentPlanCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
