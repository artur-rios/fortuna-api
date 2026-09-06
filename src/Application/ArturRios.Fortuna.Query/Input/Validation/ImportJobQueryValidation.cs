using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListImportJobsQueryValidator : AbstractValidator<ListImportJobsQuery>
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "SourceType",
        "Status",
        "CreatedAt",
        "UpdatedAt"
    };

    public ListImportJobsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(ImportJobMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(ImportJobMessages.InvalidPageSize);
        RuleFor(query => query.SourceType)
            .IsInEnum()
            .When(query => query.SourceType.HasValue)
            .WithMessage(ImportJobMessages.SourceTypeInvalid);
        RuleFor(query => query.Status)
            .IsInEnum()
            .When(query => query.Status.HasValue)
            .WithMessage(ImportJobMessages.StatusInvalid);
        RuleFor(query => query.SortBy)
            .Must(field => !string.IsNullOrWhiteSpace(field) && SortFields.Contains(field.Trim()))
            .WithMessage(ImportJobMessages.SortByUnsupported);
    }
}

public sealed class ListImportedRecordsQueryValidator : AbstractValidator<ListImportedRecordsQuery>
{
    public ListImportedRecordsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(ImportJobMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(ImportJobMessages.InvalidPageSize);
    }
}
