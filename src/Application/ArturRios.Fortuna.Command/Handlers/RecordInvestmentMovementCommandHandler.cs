using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RecordInvestmentMovementCommandHandler(
    IValidator<RecordInvestmentMovementCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IInvestmentMovementStore movements,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RecordInvestmentMovementCommand,
        RecordInvestmentMovementCommandOutput>
{
    public async Task<DataOutput<RecordInvestmentMovementCommandOutput?>> HandleAsync(
        RecordInvestmentMovementCommand command)
    {
        var output = DataOutput<RecordInvestmentMovementCommandOutput?>.New;
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
            return output.WithError(InvestmentMessages.ProfileNotFound);
        }

        var result = await movements.RecordAsync(
            new InvestmentMovementRecord(
                profile.Id,
                command.Id,
                command.MovementType,
                command.Amount,
                command.OccurredOn,
                command.FinancialAccountId,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
        if (result.Outcome != InvestmentMovementRecordOutcome.Succeeded ||
            result.Movement is null)
        {
            return output.WithError(result.Outcome switch
            {
                InvestmentMovementRecordOutcome.InvestmentNotFound =>
                    InvestmentMessages.NotFound,
                InvestmentMovementRecordOutcome.FinancialAccountNotFound =>
                    InvestmentMessages.FinancialAccountNotFound,
                InvestmentMovementRecordOutcome.ExchangeRateUnavailable =>
                    InvestmentMessages.ExchangeRateUnavailable,
                InvestmentMovementRecordOutcome.ConvertedAmountTooSmall =>
                    InvestmentMessages.ConvertedAmountTooSmall,
                _ => throw new InvalidOperationException("Unknown investment movement outcome.")
            });
        }

        var movement = result.Movement;
        return output
            .WithData(new RecordInvestmentMovementCommandOutput
            {
                Id = movement.Id,
                InvestmentId = movement.InvestmentId,
                MovementType = movement.MovementType,
                Amount = movement.Amount,
                CurrencyCode = movement.CurrencyCode,
                OccurredOn = movement.OccurredOn,
                Position = movement.Position,
                FinancialAccountId = movement.FinancialAccountId,
                FundingAmount = movement.FundingAmount,
                FundingCurrencyCode = movement.FundingCurrencyCode,
                TransferId = movement.TransferId,
                OutboundTransactionId = movement.OutboundTransactionId,
                AppliedRate = movement.AppliedRate,
                RateDate = movement.RateDate,
                CreatedAt = movement.CreatedAt,
                UpdatedAt = movement.UpdatedAt
            })
            .WithMessage(InvestmentMessages.MovementRecordedSuccessfully);
    }
}
