using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class RequestDataExportCommandValidator
    : AbstractValidator<RequestDataExportCommand>
{
    public RequestDataExportCommandValidator()
    {
        RuleFor(command => command.RecordSet)
            .NotEmpty()
            .WithMessage(TableReportMessages.RecordSetRequired);
        RuleFor(command => command.Columns)
            .NotEmpty()
            .WithMessage(TableReportMessages.ColumnsRequired)
            .Must(columns => columns.Select(column => column?.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == columns.Count)
            .WithMessage(TableReportMessages.ColumnsMustBeUnique);
        RuleForEach(command => command.Columns)
            .NotEmpty()
            .WithMessage(TableReportMessages.ColumnsRequired);
        RuleFor(command => command.Format)
            .Must(format => DataExportInput.TryParseFormat(format, out _))
            .WithMessage(DataExportMessages.FormatUnsupported);
        RuleFor(command => command.Locale)
            .Must(locale => string.IsNullOrWhiteSpace(locale) ||
                DataExportInput.TryResolveLocale(locale, out _))
            .WithMessage(DataExportMessages.LocaleInvalid);
        RuleFor(command => command.DisplayCurrencyCode)
            .OptionalCurrencyCode()
            .WithMessage(TableReportMessages.DisplayCurrencyInvalid);
        RuleForEach(command => command.Filters).ChildRules(filter =>
        {
            filter.RuleFor(item => item.Field)
                .NotEmpty().WithMessage(TableReportMessages.FilterFieldRequired);
            filter.RuleFor(item => item.Operator)
                .NotEmpty().WithMessage(TableReportMessages.FilterOperatorRequired);
            filter.RuleFor(item => item.Value)
                .NotNull().WithMessage(TableReportMessages.FilterValueRequired);
        });
        RuleForEach(command => command.Sorts).ChildRules(sort =>
            sort.RuleFor(item => item.Field)
                .NotEmpty().WithMessage(TableReportMessages.SortFieldRequired));
    }
}
