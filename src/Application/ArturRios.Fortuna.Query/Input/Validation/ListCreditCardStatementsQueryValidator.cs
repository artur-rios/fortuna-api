using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListCreditCardStatementsQueryValidator
    : AbstractValidator<ListCreditCardStatementsQuery>
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PeriodStart",
        "PeriodEnd",
        "ClosingDate",
        "DueDate",
        "Status",
        "PurchaseTotal",
        "AmountDue",
        "CreatedAt",
        "UpdatedAt"
    };

    public ListCreditCardStatementsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(CreditCardStatementMessages.InvalidPageNumber);

        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(CreditCardStatementMessages.InvalidPageSize);

        RuleFor(query => query.Status)
            .IsInEnum()
            .When(query => query.Status.HasValue)
            .WithMessage(CreditCardStatementMessages.StatusInvalid);

        RuleFor(query => query)
            .Must(query => !query.From.HasValue || !query.To.HasValue || query.From <= query.To)
            .WithMessage(CreditCardStatementMessages.PeriodInvalid);

        RuleFor(query => query.SortBy)
            .Must(field => !string.IsNullOrWhiteSpace(field) && SortFields.Contains(field.Trim()))
            .WithMessage(CreditCardStatementMessages.SortByUnsupported);
    }
}
