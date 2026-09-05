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
[Route("api/transfers")]
public sealed class TransfersController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [TransferMessages.RecordedSuccessfully] = StatusCodes.Status201Created,
            [TransferMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [TransferMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [TransferMessages.RestoredSuccessfully] = StatusCodes.Status200OK,
            [TransferMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [TransferMessages.OriginFinancialAccountNotFound] = StatusCodes.Status404NotFound,
            [TransferMessages.DestinationFinancialAccountNotFound] =
                StatusCodes.Status404NotFound,
            [TransferMessages.DestinationStatementNotFound] = StatusCodes.Status404NotFound,
            [TransferMessages.NotFound] = StatusCodes.Status404NotFound,
            [TransferMessages.AccountsMustDiffer] = StatusCodes.Status400BadRequest,
            [TransferMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
            [TransferMessages.ConvertedAmountTooSmall] = StatusCodes.Status400BadRequest,
            [TransferMessages.StatementOpen] = StatusCodes.Status409Conflict,
            [TransferMessages.StatementAlreadySettled] = StatusCodes.Status409Conflict,
            [TransferMessages.SettledStatementFrozen] = StatusCodes.Status409Conflict,
            [TransferMessages.RestoreRequiresSoftDeletion] = StatusCodes.Status409Conflict,
            [TransferMessages.TransferIdRequired] = StatusCodes.Status400BadRequest,
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

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransferOutput?>>> GetById(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetTransferByIdQuery,
            TransferOutput>(new GetTransferByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted
            });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

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

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransferLifecycleCommandOutput?>>> Delete(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            DeleteTransferCommand,
            TransferLifecycleCommandOutput>(new DeleteTransferCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransferLifecycleCommandOutput?>>> Restore(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RestoreTransferCommand,
            TransferLifecycleCommandOutput>(new RestoreTransferCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
