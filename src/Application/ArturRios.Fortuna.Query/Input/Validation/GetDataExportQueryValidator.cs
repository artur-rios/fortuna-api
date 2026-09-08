using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetDataExportQueryValidator : AbstractValidator<GetDataExportQuery>
{
    public GetDataExportQueryValidator()
    {
        RuleFor(query => query.Id)
            .NotEmpty()
            .WithMessage(DataExportMessages.NotFound);
    }
}
