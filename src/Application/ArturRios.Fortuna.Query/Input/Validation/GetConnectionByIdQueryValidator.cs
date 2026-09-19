using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetConnectionByIdQueryValidator : AbstractValidator<GetConnectionByIdQuery>
{
    public GetConnectionByIdQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(ConnectionMessages.NotFound);
    }
}
