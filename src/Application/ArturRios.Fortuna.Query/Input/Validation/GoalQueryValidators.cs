using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetGoalByIdQueryValidator : AbstractValidator<GetGoalByIdQuery>
{
    public GetGoalByIdQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(GoalMessages.NotFound);
    }
}

public sealed class GetGoalProgressQueryValidator : AbstractValidator<GetGoalProgressQuery>
{
    public GetGoalProgressQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(GoalMessages.NotFound);
    }
}

public sealed class ListGoalsQueryValidator : AbstractValidator<ListGoalsQuery>
{
    public ListGoalsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(GoalMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(GoalMessages.InvalidPageSize);
    }
}
