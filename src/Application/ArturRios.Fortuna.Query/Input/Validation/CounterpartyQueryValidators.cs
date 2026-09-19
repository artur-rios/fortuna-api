using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class SuggestCounterpartyCategoryQueryValidator : AbstractValidator<SuggestCounterpartyCategoryQuery>
{
    public SuggestCounterpartyCategoryQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(CounterpartyMessages.NotFound);
    }
}

public sealed class ListCounterpartiesQueryValidator : AbstractValidator<ListCounterpartiesQuery>
{
    public ListCounterpartiesQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(CounterpartyMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(CounterpartyMessages.InvalidPageSize);
    }
}
