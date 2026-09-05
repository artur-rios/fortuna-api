using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListInvestmentValuationsQueryValidator
    : AbstractValidator<ListInvestmentValuationsQuery>
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "ValuedOn",
        "Value",
        "CreatedAt",
        "UpdatedAt"
    };

    public ListInvestmentValuationsQueryValidator()
    {
        RuleFor(query => query.InvestmentId)
            .NotEmpty()
            .WithMessage(InvestmentMessages.InvestmentIdRequired);
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(InvestmentMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(InvestmentMessages.InvalidPageSize);
        RuleFor(query => query)
            .Must(query => !query.From.HasValue || !query.To.HasValue || query.From <= query.To)
            .WithMessage(InvestmentMessages.ValuationPeriodInvalid);
        RuleFor(query => query.SortBy)
            .Must(field => !string.IsNullOrWhiteSpace(field) && SortFields.Contains(field.Trim()))
            .WithMessage(InvestmentMessages.ValuationSortByUnsupported);
    }
}
