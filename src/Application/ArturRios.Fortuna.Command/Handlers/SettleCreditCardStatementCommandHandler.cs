using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class SettleCreditCardStatementCommandHandler(
    IValidator<SettleCreditCardStatementCommand> validator,
    ICurrentProfileResolver profileResolver,
    ICreditCardStatementSettlementStore settlements,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<SettleCreditCardStatementCommand,
        SettleCreditCardStatementCommandOutput>
{
    public async Task<DataOutput<SettleCreditCardStatementCommandOutput?>> HandleAsync(
        SettleCreditCardStatementCommand command)
    {
        var output = DataOutput<SettleCreditCardStatementCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(CreditCardStatementMessages.ProfileNotFound);
        }

        var result = await CreditCardStatementPayment.PayAsync(
            settlements,
            new CreditCardStatementSettlement(
                profile.Id,
                command.Id,
                command.FinancialAccountId,
                command.Amount,
                command.PaymentDate,
                timeProvider.GetUtcNow()));
        if (result.Settlement is null)
        {
            return output.WithError(result.Error!);
        }

        var settlement = result.Settlement;

        return output
            .WithData(new SettleCreditCardStatementCommandOutput
            {
                Id = settlement.StatementId,
                Status = settlement.Status,
                TransferId = settlement.TransferId,
                OutboundTransactionId = settlement.OutboundTransactionId,
                InboundTransactionId = settlement.InboundTransactionId,
                FinancialAccountId = settlement.FinancialAccountId,
                PaymentAmount = settlement.PaymentAmount,
                PaymentCurrencyCode = settlement.PaymentCurrencyCode,
                AppliedAmount = settlement.AppliedAmount,
                CreditCardCurrencyCode = settlement.CreditCardCurrencyCode,
                StatementAmountDue = settlement.StatementAmountDue,
                RemainingBalance = settlement.RemainingBalance,
                CarryStatementId = settlement.CarryStatementId,
                CreditAmount = settlement.CreditAmount,
                AppliedRate = settlement.AppliedRate,
                RateDate = settlement.RateDate,
                PaymentDate = settlement.PaymentDate
            })
            .WithMessage(CreditCardStatementMessages.SettledSuccessfully);
    }
}
