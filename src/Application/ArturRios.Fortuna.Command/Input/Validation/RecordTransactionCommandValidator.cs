using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class RecordTransactionCommandValidator : AbstractValidator<RecordTransactionCommand>
{
    public RecordTransactionCommandValidator(TimeProvider timeProvider)
    {
        var maximumDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);

        RuleFor(command => command.OccurredOn)
            .Cascade(CascadeMode.Stop)
            .NotEqual(default(DateOnly))
            .WithMessage(TransactionMessages.OccurredOnRequired)
            .LessThanOrEqualTo(maximumDate)
            .WithMessage(TransactionMessages.OccurredOnTooFarInFuture);
        RuleFor(command => command.Amount)
            .GreaterThan(0m)
            .WithMessage(TransactionMessages.AmountPositive);
        RuleFor(command => command.Amount)
            .Must(MoneyFitsStorage)
            .When(command => command.Amount > 0m)
            .WithMessage(TransactionMessages.AmountPrecisionInvalid);
        RuleFor(command => command.Direction)
            .IsInEnum()
            .WithMessage(TransactionMessages.DirectionInvalid);
        RuleFor(command => command)
            .Must(command =>
                (command.FinancialAccountId.HasValue && command.FinancialAccountId != Guid.Empty) ^
                (command.CreditCardId.HasValue && command.CreditCardId != Guid.Empty))
            .WithMessage(TransactionMessages.ExactlyOneTargetRequired);
        RuleFor(command => command.CategoryId)
            .NotEmpty()
            .WithMessage(TransactionMessages.CategoryIdRequired);
        RuleFor(command => command.CurrencyCode)
            .Length(3)
            .When(command => !string.IsNullOrWhiteSpace(command.CurrencyCode))
            .WithMessage(TransactionMessages.CurrencyInvalid);
        RuleFor(command => command.Description)
            .MaximumLength(500)
            .WithMessage(TransactionMessages.DescriptionTooLong);
        RuleFor(command => command.Counterparty)
            .MaximumLength(200)
            .WithMessage(TransactionMessages.CounterpartyTooLong);
        RuleFor(command => command.Tags)
            .Must(tags => tags is null || tags.Count <= 50)
            .WithMessage(TransactionMessages.TooManyTags);
        RuleForEach(command => command.Tags)
            .NotEmpty()
            .WithMessage(TransactionMessages.TagRequired)
            .MaximumLength(200)
            .WithMessage(TransactionMessages.TagTooLong);
        RuleFor(command => command.OwnerId)
            .Null()
            .WithMessage(TransactionMessages.OwnerImmutable);
    }

    private static bool MoneyFitsStorage(decimal amount) =>
        decimal.GetBits(amount)[3] >> 16 <= 4 &&
        Math.Abs(amount) < 1_000_000_000_000_000m;
}
