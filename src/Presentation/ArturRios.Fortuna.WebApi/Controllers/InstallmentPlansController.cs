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
[Route("api/installment-plans")]
public sealed class InstallmentPlansController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [InstallmentPlanMessages.RecordedSuccessfully] = StatusCodes.Status201Created,
            [InstallmentPlanMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [InstallmentPlanMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [InstallmentPlanMessages.RestoredSuccessfully] = StatusCodes.Status200OK,
            [InstallmentPlanMessages.NotFound] = StatusCodes.Status404NotFound,
            [InstallmentPlanMessages.CreditCardNotFound] = StatusCodes.Status404NotFound,
            [InstallmentPlanMessages.CategoryNotFound] = StatusCodes.Status404NotFound,
            [InstallmentPlanMessages.CurrencyNotSupported] = StatusCodes.Status400BadRequest,
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
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InstallmentPlanOutput?>>> GetById(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        return await QueryAsync<
            GetInstallmentPlanByIdQuery,
            InstallmentPlanOutput>(new GetInstallmentPlanByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted
            });
    }

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordInstallmentPlanCommandOutput?>>> Record(
        [FromBody] RecordInstallmentPlanCommand command)
    {
        return await SendAsync<
            RecordInstallmentPlanCommand,
            RecordInstallmentPlanCommandOutput>(command);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InstallmentPlanLifecycleCommandOutput?>>> Delete(
        Guid id)
    {
        return await SendAsync<
            DeleteInstallmentPlanCommand,
            InstallmentPlanLifecycleCommandOutput>(new DeleteInstallmentPlanCommand { Id = id });
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<InstallmentPlanLifecycleCommandOutput?>>> Restore(
        Guid id)
    {
        return await SendAsync<
            RestoreInstallmentPlanCommand,
            InstallmentPlanLifecycleCommandOutput>(new RestoreInstallmentPlanCommand { Id = id });
    }
}
