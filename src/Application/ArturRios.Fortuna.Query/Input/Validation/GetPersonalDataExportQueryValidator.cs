using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class GetPersonalDataExportQueryValidator : AbstractValidator<GetPersonalDataExportQuery>
{
    public GetPersonalDataExportQueryValidator()
    {
        RuleFor(query => query.JobId)
            .NotEmpty()
            .WithMessage(PersonalDataExportMessages.NotFound);
    }
}
