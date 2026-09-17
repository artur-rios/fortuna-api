using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListTransactionAttachmentsQueryValidator
    : AbstractValidator<ListTransactionAttachmentsQuery>
{
    public ListTransactionAttachmentsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(AttachmentMessages.InvalidPageNumber);

        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(AttachmentMessages.InvalidPageSize);
    }
}
