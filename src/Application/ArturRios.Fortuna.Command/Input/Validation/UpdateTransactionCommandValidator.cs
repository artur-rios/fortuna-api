using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Classification;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class UpdateTransactionCommandValidator : AbstractValidator<UpdateTransactionCommand>
{
    public UpdateTransactionCommandValidator(
        TimeProvider timeProvider,
        TagOptions? tagOptions = null)
    {
        var maximumTags = tagOptions?.MaximumPerTransaction ?? 50;
        var maximumDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(1);

        RuleFor(command => command.Id)
            .NotEmpty()
            .WithMessage(TransactionMessages.TransactionIdRequired);
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
        RuleFor(command => command.CategoryId)
            .NotEmpty()
            .WithMessage(TransactionMessages.CategoryIdRequired);
        RuleFor(command => command.Description)
            .MaximumLength(500)
            .WithMessage(TransactionMessages.DescriptionTooLong);
        RuleFor(command => command.Counterparty)
            .MaximumLength(200)
            .WithMessage(TransactionMessages.CounterpartyTooLong);
        RuleFor(command => command.Tags)
            .Must(tags => WithinTagLimit(tags, maximumTags))
            .WithMessage(TransactionMessages.TooManyTags);
        RuleFor(command => command.Tags)
            .Must(tags => WithinTagLimit(tags, maximumTags))
            .WithMessage(TransactionMessages.MaximumTagsAllowed(maximumTags));
        RuleForEach(command => command.Tags)
            .NotEmpty()
            .WithMessage(TransactionMessages.TagRequired)
            .MaximumLength(200)
            .WithMessage(TransactionMessages.TagTooLong);
        RuleFor(command => command.FinancialAccountId)
            .Null()
            .WithMessage(TransactionMessages.TransactionTargetImmutable);
        RuleFor(command => command.CreditCardId)
            .Null()
            .WithMessage(TransactionMessages.TransactionTargetImmutable);
        RuleFor(command => command.CurrencyCode)
            .Null()
            .WithMessage(TransactionMessages.TransactionCurrencyImmutable);
        RuleFor(command => command.OwnerId)
            .Null()
            .WithMessage(TransactionMessages.OwnerImmutable);
    }

    private static bool MoneyFitsStorage(decimal amount) =>
        decimal.GetBits(amount)[3] >> 16 <= 4 &&
        Math.Abs(amount) < 1_000_000_000_000_000m;

    private static bool WithinTagLimit(IReadOnlyCollection<string>? tags, int maximum) =>
        tags is null || tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(maximum + 1)
            .Count() <= maximum;
}
