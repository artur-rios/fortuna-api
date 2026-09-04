using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListCreditCardsQueryValidator : AbstractValidator<ListCreditCardsQuery>
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name",
        "Issuer",
        "CurrencyCode",
        "CreditLimit",
        "UsedAmount",
        "CreatedAt",
        "UpdatedAt"
    };

    public ListCreditCardsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(CreditCardMessages.InvalidPageNumber);

        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(CreditCardMessages.InvalidPageSize);

        RuleFor(query => query.Name)
            .MaximumLength(200)
            .WithMessage(CreditCardMessages.NameTooLong);

        RuleFor(query => query.Issuer)
            .MaximumLength(200)
            .WithMessage(CreditCardMessages.IssuerTooLong);

        RuleFor(query => query.CurrencyCode)
            .Must(code => code is null ||
                code.Trim().Length == 3 && code.Trim().All(char.IsAsciiLetter))
            .WithMessage(CreditCardMessages.CurrencyInvalid);

        RuleFor(query => query.SortBy)
            .Must(field => !string.IsNullOrWhiteSpace(field) && SortFields.Contains(field.Trim()))
            .WithMessage(CreditCardMessages.SortByUnsupported);
    }
}
