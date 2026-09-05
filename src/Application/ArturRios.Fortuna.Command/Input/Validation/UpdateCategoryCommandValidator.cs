using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEmpty()
            .WithMessage(CategoryMessages.NotFound);

        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(CategoryMessages.NameRequired)
            .MaximumLength(200)
            .WithMessage(CategoryMessages.NameTooLong);

        RuleFor(command => command.ParentId)
            .Must(parentId => parentId != Guid.Empty)
            .When(command => command.ParentId.HasValue)
            .WithMessage(CategoryMessages.ParentIdInvalid);
    }
}
