using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class RecordTransferCommandValidator : AbstractValidator<RecordTransferCommand>
{
    public RecordTransferCommandValidator(TimeProvider timeProvider)
    {
        var maximumDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);

        RuleFor(command => command.OriginFinancialAccountId)
            .NotEmpty()
            .WithMessage(TransferMessages.OriginFinancialAccountIdRequired);
        RuleFor(command => command)
            .Must(command =>
                (command.DestinationFinancialAccountId.HasValue &&
                    command.DestinationFinancialAccountId != Guid.Empty) ^
                (command.DestinationStatementId.HasValue &&
                    command.DestinationStatementId != Guid.Empty))
            .WithMessage(TransferMessages.ExactlyOneDestinationRequired);
        RuleFor(command => command)
            .Must(command => !command.DestinationFinancialAccountId.HasValue ||
                command.OriginFinancialAccountId != command.DestinationFinancialAccountId.Value)
            .WithMessage(TransferMessages.AccountsMustDiffer);
        RuleFor(command => command.Amount)
            .GreaterThan(0m)
            .WithMessage(TransferMessages.AmountPositive);
        RuleFor(command => command.Amount)
            .Must(MoneyFitsStorage)
            .When(command => command.Amount > 0m)
            .WithMessage(TransferMessages.AmountPrecisionInvalid);
        RuleFor(command => command.OccurredOn)
            .Cascade(CascadeMode.Stop)
            .NotEqual(default(DateOnly))
            .WithMessage(TransferMessages.OccurredOnRequired)
            .LessThanOrEqualTo(maximumDate)
            .WithMessage(TransferMessages.OccurredOnTooFarInFuture);
        RuleFor(command => command.OwnerId)
            .Null()
            .WithMessage(TransferMessages.OwnerImmutable);
    }

    private static bool MoneyFitsStorage(decimal amount) =>
        decimal.GetBits(amount)[3] >> 16 <= 4 &&
        Math.Abs(amount) < 1_000_000_000_000_000m;
}
