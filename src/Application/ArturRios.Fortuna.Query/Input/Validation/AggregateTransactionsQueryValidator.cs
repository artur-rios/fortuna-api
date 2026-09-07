using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Reporting;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class AggregateTransactionsQueryValidator : AbstractValidator<AggregateTransactionsQuery>
{
    public static readonly IReadOnlyCollection<string> SupportedDimensions =
        ["period", "category", "account", "card", "counterparty", "tag"];

    public static readonly IReadOnlyCollection<string> SupportedGranularities =
        ["day", "week", "month", "quarter", "year"];

    public AggregateTransactionsQueryValidator(TransactionAggregationOptions options)
    {
        RuleFor(query => query.Dimension)
            .NotEmpty()
            .WithMessage(TransactionAggregationMessages.DimensionRequired)
            .Must(IsSupportedDimension)
            .WithMessage(query => TransactionAggregationMessages.UnknownDimension(
                query.Dimension,
                SupportedDimensions));

        RuleFor(query => query.Granularity)
            .NotEmpty()
            .When(query => IsPeriod(query.Dimension))
            .WithMessage(TransactionAggregationMessages.GranularityRequired);
        RuleFor(query => query.Granularity)
            .Must(value => string.IsNullOrWhiteSpace(value) || IsSupportedGranularity(value))
            .WithMessage(query => TransactionAggregationMessages.UnknownGranularity(
                query.Granularity ?? string.Empty,
                SupportedGranularities));

        RuleFor(query => query.From)
            .NotNull()
            .WithMessage(TransactionAggregationMessages.FromRequired);
        RuleFor(query => query.To)
            .NotNull()
            .WithMessage(TransactionAggregationMessages.ToRequired);
        RuleFor(query => query)
            .Must(query => !query.From.HasValue || !query.To.HasValue || query.From <= query.To)
            .WithMessage(TransactionAggregationMessages.DateRangeInvalid);
        RuleFor(query => query)
            .Must(query => !query.From.HasValue || !query.To.HasValue || query.From > query.To ||
                query.To.Value.DayNumber - query.From.Value.DayNumber < options.MaximumSpanDays)
            .WithMessage(TransactionAggregationMessages.MaximumSpanExceeded(options.MaximumSpanDays));

        RuleFor(query => query.FinancialAccountId)
            .Must(IsOptionalIdentifier)
            .WithMessage(TransactionAggregationMessages.FinancialAccountIdInvalid);
        RuleFor(query => query.CreditCardId)
            .Must(IsOptionalIdentifier)
            .WithMessage(TransactionAggregationMessages.CreditCardIdInvalid);
        RuleFor(query => query.CategoryId)
            .Must(IsOptionalIdentifier)
            .WithMessage(TransactionAggregationMessages.CategoryIdInvalid);
        RuleFor(query => query.TagId)
            .Must(IsOptionalIdentifier)
            .WithMessage(TransactionAggregationMessages.TagIdInvalid);
        RuleFor(query => query.CounterpartyId)
            .Must(IsOptionalIdentifier)
            .WithMessage(TransactionAggregationMessages.CounterpartyIdInvalid);
        RuleFor(query => query.Direction)
            .IsInEnum()
            .When(query => query.Direction.HasValue)
            .WithMessage(TransactionAggregationMessages.DirectionInvalid);
        RuleFor(query => query.MinimumAmount)
            .GreaterThanOrEqualTo(0m)
            .When(query => query.MinimumAmount.HasValue)
            .WithMessage(TransactionAggregationMessages.MinimumAmountInvalid);
        RuleFor(query => query.MaximumAmount)
            .GreaterThanOrEqualTo(0m)
            .When(query => query.MaximumAmount.HasValue)
            .WithMessage(TransactionAggregationMessages.MaximumAmountInvalid);
        RuleFor(query => query.MinimumAmount)
            .PrecisionScale(19, 4, false)
            .When(query => query.MinimumAmount.HasValue && query.MinimumAmount >= 0m)
            .WithMessage(TransactionAggregationMessages.AmountPrecisionInvalid);
        RuleFor(query => query.MaximumAmount)
            .PrecisionScale(19, 4, false)
            .When(query => query.MaximumAmount.HasValue && query.MaximumAmount >= 0m)
            .WithMessage(TransactionAggregationMessages.AmountPrecisionInvalid);
        RuleFor(query => query)
            .Must(query => !query.MinimumAmount.HasValue || !query.MaximumAmount.HasValue ||
                query.MinimumAmount <= query.MaximumAmount)
            .WithMessage(TransactionAggregationMessages.AmountRangeInvalid);
        RuleFor(query => query.Text)
            .MaximumLength(500)
            .WithMessage(TransactionAggregationMessages.TextTooLong);
        RuleFor(query => query.DisplayCurrencyCode)
            .Must(code => code is null ||
                code.Trim().Length == 3 && code.Trim().All(char.IsAsciiLetter))
            .WithMessage(TransactionAggregationMessages.DisplayCurrencyInvalid);
    }

    private static bool IsPeriod(string? value) =>
        string.Equals(value?.Trim(), "period", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedDimension(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        SupportedDimensions.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool IsSupportedGranularity(string value) =>
        SupportedGranularities.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool IsOptionalIdentifier(Guid? value) => value is null || value != Guid.Empty;
}
