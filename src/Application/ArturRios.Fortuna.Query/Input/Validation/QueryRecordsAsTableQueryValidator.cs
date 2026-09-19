using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class QueryRecordsAsTableQueryValidator : AbstractValidator<QueryRecordsAsTableQuery>
{
    public QueryRecordsAsTableQueryValidator()
    {
        RuleFor(query => query.RecordSet)
            .NotEmpty()
            .WithMessage(TableReportMessages.RecordSetRequired);
        RuleFor(query => query.Columns)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(TableReportMessages.ColumnsRequired)
            .Must(columns => columns
                .Select(column => column?.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == columns.Count)
            .WithMessage(TableReportMessages.ColumnsMustBeUnique);
        RuleForEach(query => query.Columns)
            .NotEmpty()
            .WithMessage(TableReportMessages.ColumnsRequired);
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(TableReportMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(TableReportMessages.InvalidPageSize);
        RuleFor(query => query.Filters)
            .NotNull()
            .WithMessage(TableReportMessages.FiltersRequired);
        RuleForEach(query => query.Filters)
            .NotNull()
            .WithMessage(TableReportMessages.FilterRequired)
            .ChildRules(filter =>
            {
                filter.RuleFor(item => item.Field)
                    .NotEmpty()
                    .WithMessage(TableReportMessages.FilterFieldRequired);
                filter.RuleFor(item => item.Operator)
                    .NotEmpty()
                    .WithMessage(TableReportMessages.FilterOperatorRequired);
                filter.RuleFor(item => item.Value)
                    .NotNull()
                    .WithMessage(TableReportMessages.FilterValueRequired);
            });
        RuleFor(query => query.Sorts)
            .NotNull()
            .WithMessage(TableReportMessages.SortsRequired);
        RuleForEach(query => query.Sorts)
            .NotNull()
            .WithMessage(TableReportMessages.SortRequired)
            .ChildRules(sort =>
                sort.RuleFor(item => item.Field)
                    .NotEmpty()
                    .WithMessage(TableReportMessages.SortFieldRequired));
        RuleFor(query => query.DisplayCurrencyCode)
            .OptionalCurrencyCode()
            .WithMessage(TableReportMessages.DisplayCurrencyInvalid);
    }
}
