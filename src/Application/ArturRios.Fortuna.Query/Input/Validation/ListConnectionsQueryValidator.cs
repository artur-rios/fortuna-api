using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class ListConnectionsQueryValidator : AbstractValidator<ListConnectionsQuery>
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "DataSourceType",
        "Status",
        "CreatedAt",
        "UpdatedAt"
    };

    public ListConnectionsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(ConnectionMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(ConnectionMessages.InvalidPageSize);
        RuleFor(query => query.DataSourceType)
            .IsInEnum()
            .When(query => query.DataSourceType.HasValue)
            .WithMessage(ConnectionMessages.DataSourceTypeInvalid);
        RuleFor(query => query.Status)
            .IsInEnum()
            .When(query => query.Status.HasValue)
            .WithMessage(ConnectionMessages.StatusInvalid);
        RuleFor(query => query.SortBy)
            .Must(field => !string.IsNullOrWhiteSpace(field) && SortFields.Contains(field.Trim()))
            .WithMessage(ConnectionMessages.SortByUnsupported);
    }
}
