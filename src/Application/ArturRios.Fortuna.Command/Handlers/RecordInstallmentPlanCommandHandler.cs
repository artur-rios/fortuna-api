using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Handlers;

public sealed class RecordInstallmentPlanCommandHandler(
    IValidator<RecordInstallmentPlanCommand> validator,
    IRequestActorAccessor actorAccessor,
    IUserProfileReader profiles,
    IInstallmentPlanStore plans,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<RecordInstallmentPlanCommand, RecordInstallmentPlanCommandOutput>
{
    public async Task<DataOutput<RecordInstallmentPlanCommandOutput?>> HandleAsync(
        RecordInstallmentPlanCommand command)
    {
        var output = DataOutput<RecordInstallmentPlanCommandOutput?>.New;
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var profile = await InstallmentPlanHandler.ResolveProfileAsync(
            actorAccessor.Actor,
            profiles);
        if (profile is null)
        {
            return output.WithError(InstallmentPlanMessages.ProfileNotFound);
        }

        var result = await plans.RecordAsync(new InstallmentPlanRecord(
            profile.Id,
            command.CreditCardId,
            command.CategoryId,
            command.TotalAmount,
            command.InstallmentCount,
            command.PurchasedOn,
            command.CurrencyCode,
            command.Counterparty,
            timeProvider.GetUtcNow()), CancellationToken.None);
        if (result.Outcome != InstallmentPlanRecordOutcome.Succeeded || result.Plan is null)
        {
            return output.WithError(result.Outcome switch
            {
                InstallmentPlanRecordOutcome.CreditCardNotFound =>
                    InstallmentPlanMessages.CreditCardNotFound,
                InstallmentPlanRecordOutcome.CategoryNotFound =>
                    InstallmentPlanMessages.CategoryNotFound,
                InstallmentPlanRecordOutcome.CurrencyNotSupported =>
                    InstallmentPlanMessages.CurrencyNotSupported,
                InstallmentPlanRecordOutcome.ExchangeRateUnavailable =>
                    InstallmentPlanMessages.ExchangeRateUnavailable,
                InstallmentPlanRecordOutcome.AmountTooSmall =>
                    InstallmentPlanMessages.AmountTooSmall,
                _ => throw new InvalidOperationException("Unknown installment plan outcome.")
            });
        }

        return output
            .WithData(Map(result.Plan))
            .WithMessage(InstallmentPlanMessages.RecordedSuccessfully);
    }

    private static RecordInstallmentPlanCommandOutput Map(InstallmentPlanSnapshot plan) => new()
    {
        Id = plan.Id,
        CreditCardId = plan.CreditCardId,
        TotalAmount = plan.TotalAmount,
        CurrencyCode = plan.CurrencyCode,
        OriginalTotalAmount = plan.OriginalTotalAmount,
        OriginalCurrencyCode = plan.OriginalCurrencyCode,
        AppliedRate = plan.AppliedRate,
        RateDate = plan.RateDate,
        InstallmentCount = plan.InstallmentCount,
        PurchasedOn = plan.PurchasedOn,
        Installments = plan.Installments.Select(item => new InstallmentCommandOutput
        {
            TransactionId = item.TransactionId,
            Number = item.Number,
            Amount = item.Amount,
            CurrencyCode = item.CurrencyCode,
            OriginalAmount = item.OriginalAmount,
            OriginalCurrencyCode = item.OriginalCurrencyCode,
            AppliedRate = item.AppliedRate,
            RateDate = item.RateDate,
            OccurredOn = item.OccurredOn,
            StatementId = item.StatementId,
            IsLateArriving = item.IsLateArriving
        }).ToArray()
    };
}
