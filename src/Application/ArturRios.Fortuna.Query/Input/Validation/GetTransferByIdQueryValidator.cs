using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetTransferByIdQueryValidator : AbstractValidator<GetTransferByIdQuery>
{
    public GetTransferByIdQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(TransferMessages.TransferIdRequired);
    }
}
