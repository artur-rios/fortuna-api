using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class SearchTransactionsQueryValidator : AbstractValidator<SearchTransactionsQuery>
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "OccurredOn",
        "Amount",
        "Direction",
        "Category",
        "Counterparty",
        "CurrencyCode",
        "Description",
        "CreatedAt",
        "UpdatedAt"
    };

    public SearchTransactionsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(TransactionMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(TransactionMessages.InvalidPageSize);
        RuleFor(query => query)
            .Must(query => !query.From.HasValue || !query.To.HasValue || query.From <= query.To)
            .WithMessage(TransactionMessages.DateRangeInvalid);
        RuleFor(query => query.FinancialAccountId)
            .Must(IsOptionalIdentifier)
            .WithMessage(TransactionMessages.FinancialAccountIdInvalid);
        RuleFor(query => query.CreditCardId)
            .Must(IsOptionalIdentifier)
            .WithMessage(TransactionMessages.CreditCardIdInvalid);
        RuleFor(query => query.CategoryId)
            .Must(IsOptionalIdentifier)
            .WithMessage(TransactionMessages.CategoryFilterIdInvalid);
        RuleFor(query => query.TagId)
            .Must(IsOptionalIdentifier)
            .WithMessage(TransactionMessages.TagIdInvalid);
        RuleFor(query => query.CounterpartyId)
            .Must(IsOptionalIdentifier)
            .WithMessage(TransactionMessages.CounterpartyIdInvalid);
        RuleFor(query => query.Direction)
            .IsInEnum()
            .When(query => query.Direction.HasValue)
            .WithMessage(TransactionMessages.DirectionInvalid);
        RuleFor(query => query.MinimumAmount)
            .GreaterThanOrEqualTo(0m)
            .When(query => query.MinimumAmount.HasValue)
            .WithMessage(TransactionMessages.MinimumAmountInvalid);
        RuleFor(query => query.MaximumAmount)
            .GreaterThanOrEqualTo(0m)
            .When(query => query.MaximumAmount.HasValue)
            .WithMessage(TransactionMessages.MaximumAmountInvalid);
        RuleFor(query => query.MinimumAmount)
            .PrecisionScale(19, 4, false)
            .When(query => query.MinimumAmount.HasValue && query.MinimumAmount >= 0m)
            .WithMessage(TransactionMessages.AmountPrecisionInvalid);
        RuleFor(query => query.MaximumAmount)
            .PrecisionScale(19, 4, false)
            .When(query => query.MaximumAmount.HasValue && query.MaximumAmount >= 0m)
            .WithMessage(TransactionMessages.AmountPrecisionInvalid);
        RuleFor(query => query)
            .Must(query =>
                !query.MinimumAmount.HasValue ||
                !query.MaximumAmount.HasValue ||
                query.MinimumAmount <= query.MaximumAmount)
            .WithMessage(TransactionMessages.AmountRangeInvalid);
        RuleFor(query => query.Text)
            .MaximumLength(500)
            .WithMessage(TransactionMessages.SearchTextTooLong);
        RuleFor(query => query.DisplayCurrencyCode)
            .Length(3)
            .When(query => !string.IsNullOrWhiteSpace(query.DisplayCurrencyCode))
            .WithMessage(TransactionMessages.DisplayCurrencyInvalid);
        RuleFor(query => query.SortBy)
            .Must(field => !string.IsNullOrWhiteSpace(field) && SortFields.Contains(field.Trim()))
            .WithMessage(TransactionMessages.SortByUnsupported);
    }

    private static bool IsOptionalIdentifier(Guid? id) => id is null || id != Guid.Empty;
}
