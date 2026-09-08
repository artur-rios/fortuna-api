using System.Globalization;
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
            .Must(IsSupportedFormat)
            .WithMessage(DataExportMessages.FormatUnsupported);
        RuleFor(command => command.Locale)
            .Must(locale => locale is null || IsSpecificCulture(locale))
            .WithMessage(DataExportMessages.LocaleInvalid);
        RuleFor(command => command.DisplayCurrencyCode)
            .Must(code => code is null ||
                code.Trim().Length == 3 && code.Trim().All(char.IsAsciiLetter))
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

    private static bool IsSupportedFormat(string? format) =>
        format?.Trim().ToLowerInvariant() is "csv" or "xlsx" or "excel" or "pdf";

    private static bool IsSpecificCulture(string locale)
    {
        try
        {
            return !CultureInfo.GetCultureInfo(locale.Trim()).IsNeutralCulture;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
