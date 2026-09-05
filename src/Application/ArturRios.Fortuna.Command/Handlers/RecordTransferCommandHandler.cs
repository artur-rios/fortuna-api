using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RecordTransferCommandHandler(
    IValidator<RecordTransferCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    ITransferStore transfers,
    ICreditCardStatementSettlementStore settlements,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RecordTransferCommand, RecordTransferCommandOutput>
{
    public async Task<DataOutput<RecordTransferCommandOutput?>> HandleAsync(
        RecordTransferCommand command)
    {
        var output = DataOutput<RecordTransferCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
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

        var createdAt = timeProvider.GetUtcNow();
        return command.DestinationStatementId.HasValue
            ? await RecordStatementSettlementAsync(command, profile.Id, createdAt, output)
            : await RecordAccountTransferAsync(command, profile.Id, createdAt, output);
    }

    private async Task<DataOutput<RecordTransferCommandOutput?>> RecordAccountTransferAsync(
        RecordTransferCommand command,
        Guid userId,
        DateTimeOffset createdAt,
        DataOutput<RecordTransferCommandOutput?> output)
    {
        var result = await transfers.RecordAsync(
            new TransferRecord(
                userId,
                command.OriginFinancialAccountId,
                command.DestinationFinancialAccountId!.Value,
                command.Amount,
                command.OccurredOn,
                createdAt),
            CancellationToken.None);
        if (result.Outcome != TransferRecordOutcome.Succeeded || result.Transfer is null)
        {
            return output.WithError(result.Outcome switch
            {
                TransferRecordOutcome.OriginFinancialAccountNotFound =>
                    TransferMessages.OriginFinancialAccountNotFound,
                TransferRecordOutcome.DestinationFinancialAccountNotFound =>
                    TransferMessages.DestinationFinancialAccountNotFound,
                TransferRecordOutcome.AccountsMustDiffer => TransferMessages.AccountsMustDiffer,
                TransferRecordOutcome.ExchangeRateUnavailable =>
                    TransferMessages.ExchangeRateUnavailable,
                TransferRecordOutcome.ConvertedAmountTooSmall =>
                    TransferMessages.ConvertedAmountTooSmall,
                _ => throw new InvalidOperationException("Unknown transfer record outcome.")
            });
        }

        var transfer = result.Transfer;
        return output
            .WithData(new RecordTransferCommandOutput
            {
                Id = transfer.Id,
                OutboundTransactionId = transfer.OutboundTransactionId,
                InboundTransactionId = transfer.InboundTransactionId,
                OriginFinancialAccountId = transfer.OriginFinancialAccountId,
                DestinationFinancialAccountId = transfer.DestinationFinancialAccountId,
                OutboundAmount = transfer.OutboundAmount,
                OutboundCurrencyCode = transfer.OutboundCurrencyCode,
                InboundAmount = transfer.InboundAmount,
                InboundCurrencyCode = transfer.InboundCurrencyCode,
                AppliedRate = transfer.AppliedRate,
                RateDate = transfer.RateDate,
                OccurredOn = transfer.OccurredOn,
                CreatedAt = transfer.CreatedAt
            })
            .WithMessage(TransferMessages.RecordedSuccessfully);
    }

    private async Task<DataOutput<RecordTransferCommandOutput?>> RecordStatementSettlementAsync(
        RecordTransferCommand command,
        Guid userId,
        DateTimeOffset createdAt,
        DataOutput<RecordTransferCommandOutput?> output)
    {
        var result = await settlements.SettleAsync(
            new CreditCardStatementSettlement(
                userId,
                command.DestinationStatementId!.Value,
                command.OriginFinancialAccountId,
                command.Amount,
                command.OccurredOn,
                createdAt),
            CancellationToken.None);
        if (result.Outcome != CreditCardStatementSettlementOutcome.Succeeded ||
            result.Settlement is null)
        {
            return output.WithError(result.Outcome switch
            {
                CreditCardStatementSettlementOutcome.StatementNotFound =>
                    TransferMessages.DestinationStatementNotFound,
                CreditCardStatementSettlementOutcome.FinancialAccountNotFound =>
                    TransferMessages.OriginFinancialAccountNotFound,
                CreditCardStatementSettlementOutcome.StatementOpen => TransferMessages.StatementOpen,
                CreditCardStatementSettlementOutcome.StatementAlreadySettled =>
                    TransferMessages.StatementAlreadySettled,
                CreditCardStatementSettlementOutcome.ExchangeRateUnavailable =>
                    TransferMessages.ExchangeRateUnavailable,
                _ => throw new InvalidOperationException("Unknown statement settlement outcome.")
            });
        }

        var settlement = result.Settlement;
        return output
            .WithData(new RecordTransferCommandOutput
            {
                Id = settlement.TransferId,
                OutboundTransactionId = settlement.OutboundTransactionId,
                InboundTransactionId = settlement.InboundTransactionId,
                OriginFinancialAccountId = settlement.FinancialAccountId,
                DestinationStatementId = settlement.StatementId,
                OutboundAmount = settlement.PaymentAmount,
                OutboundCurrencyCode = settlement.PaymentCurrencyCode,
                InboundAmount = settlement.AppliedAmount,
                InboundCurrencyCode = settlement.CreditCardCurrencyCode,
                AppliedRate = settlement.AppliedRate,
                RateDate = settlement.RateDate,
                OccurredOn = settlement.PaymentDate,
                CreatedAt = createdAt
            })
            .WithMessage(TransferMessages.RecordedSuccessfully);
    }
}
