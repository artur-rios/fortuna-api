using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class CreateConnectionCommandValidator : AbstractValidator<CreateConnectionCommand>
{
    public CreateConnectionCommandValidator()
    {
        RuleFor(command => command.DataSource)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ConnectionMessages.DataSourceRequired)
            .Must(value => string.Equals(value?.Trim(), "pluggy", StringComparison.OrdinalIgnoreCase))
            .WithMessage(ConnectionMessages.DataSourceInvalid);
        RuleFor(command => command.ExternalReference)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ConnectionMessages.ExternalReferenceRequired)
            .Must(value => Guid.TryParse(value?.Trim(), out _))
            .WithMessage(ConnectionMessages.ExternalReferenceInvalid);
        RuleFor(command => command.AdditionalFields)
            .Must(fields => fields is null || fields.Count == 0)
            .WithMessage(ConnectionMessages.BankCredentialRejected);
    }
}
