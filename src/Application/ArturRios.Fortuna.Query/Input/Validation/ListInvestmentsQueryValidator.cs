using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListInvestmentsQueryValidator : AbstractValidator<ListInvestmentsQuery>
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Instrument",
        "Institution",
        "InvestmentType",
        "CurrencyCode",
        "Position",
        "CreatedAt",
        "UpdatedAt"
    };

    public ListInvestmentsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(InvestmentMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(InvestmentMessages.InvalidPageSize);
        RuleFor(query => query.Instrument)
            .MaximumLength(200)
            .WithMessage(InvestmentMessages.InstrumentTooLong);
        RuleFor(query => query.Institution)
            .MaximumLength(200)
            .WithMessage(InvestmentMessages.InstitutionTooLong);
        RuleFor(query => query.InvestmentType)
            .IsInEnum()
            .When(query => query.InvestmentType.HasValue)
            .WithMessage(InvestmentMessages.InvestmentTypeInvalid);
        RuleFor(query => query.CurrencyCode)
            .Must(InvestmentQueryValidation.IsOptionalCurrencyCode)
            .WithMessage(InvestmentMessages.CurrencyInvalid);
        RuleFor(query => query.DisplayCurrencyCode)
            .Must(InvestmentQueryValidation.IsOptionalCurrencyCode)
            .WithMessage(InvestmentMessages.DisplayCurrencyInvalid);
        RuleFor(query => query.SortBy)
            .Must(field => !string.IsNullOrWhiteSpace(field) && SortFields.Contains(field.Trim()))
            .WithMessage(InvestmentMessages.SortByUnsupported);
    }
}
