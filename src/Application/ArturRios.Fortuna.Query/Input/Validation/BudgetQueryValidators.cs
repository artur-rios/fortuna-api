using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetBudgetByIdQueryValidator : AbstractValidator<GetBudgetByIdQuery>
{
    public GetBudgetByIdQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(BudgetMessages.NotFound);
    }
}

public sealed class GetBudgetConsumptionQueryValidator : AbstractValidator<GetBudgetConsumptionQuery>
{
    public GetBudgetConsumptionQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(BudgetMessages.NotFound);
    }
}

public sealed class ListBudgetsQueryValidator : AbstractValidator<ListBudgetsQuery>
{
    public ListBudgetsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(BudgetMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(BudgetMessages.InvalidPageSize);
    }
}
