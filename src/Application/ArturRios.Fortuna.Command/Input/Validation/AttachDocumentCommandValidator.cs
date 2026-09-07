using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class AttachDocumentCommandValidator : AbstractValidator<AttachDocumentCommand>
{
    public AttachDocumentCommandValidator(AttachmentOptions options)
    {
        RuleFor(command => command.TransactionId)
            .NotEmpty()
            .WithMessage(AttachmentMessages.TransactionNotFound);
        RuleFor(command => command.Content)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(AttachmentMessages.FileRequired)
            .Must(content => content.Length <= options.MaximumBytes)
            .WithMessage(AttachmentMessages.FileTooLarge(options.MaximumBytes));
        RuleFor(command => command.FileName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(AttachmentMessages.FileNameRequired)
            .MaximumLength(300)
            .WithMessage(AttachmentMessages.FileNameTooLong);
        RuleFor(command => command.ContentType)
            .Must(contentType => options.AllowedContentTypes.Contains(
                contentType?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase))
            .WithMessage(AttachmentMessages.ContentTypeNotAllowed(options.AllowedContentTypes));
    }
}
