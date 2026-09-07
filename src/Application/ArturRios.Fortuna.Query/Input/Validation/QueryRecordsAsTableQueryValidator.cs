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

        RuleForEach(query => query.Filters).ChildRules(filter =>
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

        RuleForEach(query => query.Sorts).ChildRules(sort =>
            sort.RuleFor(item => item.Field)
                .NotEmpty()
                .WithMessage(TableReportMessages.SortFieldRequired));

        RuleFor(query => query.DisplayCurrencyCode)
            .Must(code => code is null ||
                code.Trim().Length == 3 && code.Trim().All(char.IsAsciiLetter))
            .WithMessage(TableReportMessages.DisplayCurrencyInvalid);
    }
}
