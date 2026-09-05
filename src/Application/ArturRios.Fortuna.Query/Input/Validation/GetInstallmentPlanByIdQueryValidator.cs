using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetInstallmentPlanByIdQueryValidator
    : AbstractValidator<GetInstallmentPlanByIdQuery>
{
    public GetInstallmentPlanByIdQueryValidator() => RuleFor(query => query.Id)
        .NotEmpty()
        .WithMessage(InstallmentPlanMessages.IdRequired);
}
