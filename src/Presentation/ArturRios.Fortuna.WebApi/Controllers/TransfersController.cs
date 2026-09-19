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
[Route("api/transfers")]
public sealed class TransfersController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [TransferMessages.RecordedSuccessfully] = StatusCodes.Status201Created,
            [TransferMessages.RetrievedSuccessfully] = StatusCodes.Status200OK,
            [TransferMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [TransferMessages.RestoredSuccessfully] = StatusCodes.Status200OK,
            [TransferMessages.OriginFinancialAccountNotFound] = StatusCodes.Status404NotFound,
            [TransferMessages.DestinationFinancialAccountNotFound] =
                StatusCodes.Status404NotFound,
            [CreditCardStatementMessages.NotFound] = StatusCodes.Status404NotFound,
            [CreditCardStatementMessages.FinancialAccountNotFound] = StatusCodes.Status404NotFound,
            [TransferMessages.NotFound] = StatusCodes.Status404NotFound,
            [TransferMessages.AccountsMustDiffer] = StatusCodes.Status400BadRequest,
            [TransferMessages.ConvertedAmountTooSmall] = StatusCodes.Status400BadRequest,
            [CreditCardStatementMessages.StatementOpen] = StatusCodes.Status409Conflict,
            [CreditCardStatementMessages.StatementAlreadySettled] = StatusCodes.Status409Conflict,
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
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransferOutput?>>> GetById(
        Guid id,
        [FromQuery] bool includeDeleted = false)
    {
        return await QueryAsync<
            GetTransferByIdQuery,
            TransferOutput>(new GetTransferByIdQuery
            {
                Id = id,
                IncludeDeleted = includeDeleted
            });
    }

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RecordTransferCommandOutput?>>> Record(
        [FromBody] RecordTransferCommand command)
    {
        return await SendAsync<
            RecordTransferCommand,
            RecordTransferCommandOutput>(command);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransferLifecycleCommandOutput?>>> Delete(Guid id)
    {
        return await SendAsync<
            DeleteTransferCommand,
            TransferLifecycleCommandOutput>(new DeleteTransferCommand { Id = id });
    }

    [HttpPost("{id:guid}/restore")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<TransferLifecycleCommandOutput?>>> Restore(Guid id)
    {
        return await SendAsync<
            RestoreTransferCommand,
            TransferLifecycleCommandOutput>(new RestoreTransferCommand { Id = id });
    }
}
