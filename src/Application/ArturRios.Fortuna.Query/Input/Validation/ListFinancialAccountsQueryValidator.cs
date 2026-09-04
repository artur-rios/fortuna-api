using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListFinancialAccountsQueryValidator : AbstractValidator<ListFinancialAccountsQuery>
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name",
        "Institution",
        "AccountType",
        "CurrencyCode",
        "OpeningBalance",
        "CreatedAt",
        "UpdatedAt"
    };

    public ListFinancialAccountsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(FinancialAccountMessages.InvalidPageNumber);

        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(FinancialAccountMessages.InvalidPageSize);

        RuleFor(query => query.Name)
            .MaximumLength(200)
            .WithMessage(FinancialAccountMessages.NameTooLong);

        RuleFor(query => query.Institution)
            .MaximumLength(200)
            .WithMessage(FinancialAccountMessages.InstitutionTooLong);

        RuleFor(query => query.AccountType)
            .IsInEnum()
            .When(query => query.AccountType.HasValue)
            .WithMessage(FinancialAccountMessages.AccountTypeInvalid);

        RuleFor(query => query.CurrencyCode)
            .Must(code => code is null ||
                code.Trim().Length == 3 && code.Trim().All(char.IsAsciiLetter))
            .WithMessage(FinancialAccountMessages.CurrencyInvalid);

        RuleFor(query => query.SortBy)
            .Must(field => !string.IsNullOrWhiteSpace(field) && SortFields.Contains(field.Trim()))
            .WithMessage(FinancialAccountMessages.SortByUnsupported);
    }
}
