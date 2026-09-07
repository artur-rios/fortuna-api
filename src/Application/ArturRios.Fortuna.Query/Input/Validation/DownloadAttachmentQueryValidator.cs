using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class DownloadAttachmentQueryValidator : AbstractValidator<DownloadAttachmentQuery>
{
    public DownloadAttachmentQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(AttachmentMessages.AttachmentNotFound);
    }
}
