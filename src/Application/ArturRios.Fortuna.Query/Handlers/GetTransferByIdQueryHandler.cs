using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetTransferByIdQueryHandler(
    IValidator<GetTransferByIdQuery> validator,
    IUserProfileReader profiles,
    ITransferReader transfers,
    IRequestActorAccessor actorAccessor)
    : IQueryHandlerAsync<GetTransferByIdQuery, TransferOutput>
{
    public async Task<DataOutput<TransferOutput?>> HandleAsync(GetTransferByIdQuery query)
    {
        var output = DataOutput<TransferOutput?>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
        if (profile is null)
        {
            return output.WithError(TransferMessages.ProfileNotFound);
        }

        var transfer = await transfers.FindByIdAsync(
            profile.Id,
            query.Id,
            query.IncludeDeleted,
            CancellationToken.None);
        if (transfer is null)
        {
            return output.WithError(TransferMessages.NotFound);
        }

        return output
            .WithData(new TransferOutput
            {
                Id = transfer.Id,
                OutboundTransactionId = transfer.OutboundTransactionId,
                InboundTransactionId = transfer.InboundTransactionId,
                InboundInvestmentMovementId = transfer.InboundInvestmentMovementId,
                OriginFinancialAccountId = transfer.OriginFinancialAccountId,
                DestinationFinancialAccountId = transfer.DestinationFinancialAccountId,
                DestinationCreditCardId = transfer.DestinationCreditCardId,
                DestinationStatementId = transfer.DestinationStatementId,
                DestinationInvestmentId = transfer.DestinationInvestmentId,
                OutboundAmount = transfer.OutboundAmount,
                OutboundCurrencyCode = transfer.OutboundCurrencyCode,
                InboundAmount = transfer.InboundAmount,
                InboundCurrencyCode = transfer.InboundCurrencyCode,
                AppliedRate = transfer.AppliedRate,
                RateDate = transfer.RateDate,
                OccurredOn = transfer.OccurredOn,
                OutboundIsDeleted = transfer.OutboundIsDeleted,
                InboundIsDeleted = transfer.InboundIsDeleted,
                IsDeleted = transfer.IsDeleted,
                CreatedAt = transfer.CreatedAt,
                UpdatedAt = transfer.UpdatedAt
            })
            .WithMessage(TransferMessages.RetrievedSuccessfully);
    }
}
