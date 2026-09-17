using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListRecurringTransactionsQueryValidator
    : AbstractValidator<ListRecurringTransactionsQuery>
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartsOn",
        "EndsOn",
        "Amount",
        "Frequency",
        "CreatedAt",
        "UpdatedAt"
    };

    public ListRecurringTransactionsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(RecurringTransactionMessages.InvalidPageNumber);

        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(RecurringTransactionMessages.InvalidPageSize);

        RuleFor(query => query.SortBy)
            .Must(field => !string.IsNullOrWhiteSpace(field) && SortFields.Contains(field.Trim()))
            .WithMessage(RecurringTransactionMessages.SortByUnsupported);
    }
}
