using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class CreateTagCommandValidator : AbstractValidator<CreateTagCommand>
{
    public CreateTagCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(TagMessages.NameRequired)
            .MaximumLength(200)
            .WithMessage(TagMessages.NameTooLong);
    }
}

public sealed class UpdateTagCommandValidator : AbstractValidator<UpdateTagCommand>
{
    public UpdateTagCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(TagMessages.NameRequired)
            .MaximumLength(200)
            .WithMessage(TagMessages.NameTooLong);
    }
}

public sealed class AttachTransactionTagCommandValidator
    : AbstractValidator<AttachTransactionTagCommand>
{
    public AttachTransactionTagCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEmpty()
            .WithMessage(TagMessages.TransactionIdInvalid);
        RuleFor(command => command.TagId)
            .NotEmpty()
            .WithMessage(TagMessages.TagIdInvalid);
    }
}

public sealed class DetachTransactionTagCommandValidator
    : AbstractValidator<DetachTransactionTagCommand>
{
    public DetachTransactionTagCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEmpty()
            .WithMessage(TagMessages.TransactionIdInvalid);
        RuleFor(command => command.TagId)
            .NotEmpty()
            .WithMessage(TagMessages.TagIdInvalid);
    }
}
