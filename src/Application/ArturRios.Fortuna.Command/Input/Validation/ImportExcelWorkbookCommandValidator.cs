using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class ImportExcelWorkbookCommandValidator
    : AbstractValidator<ImportExcelWorkbookCommand>
{
    public ImportExcelWorkbookCommandValidator(ExcelImportOptions options)
    {
        RuleFor(command => command.TargetId)
            .NotEmpty()
            .WithMessage(ExcelImportMessages.TargetNotFound);
        RuleFor(command => command.TargetType)
            .IsInEnum()
            .WithMessage(ExcelImportMessages.TargetTypeInvalid);
        RuleFor(command => command.Content)
            .NotEmpty()
            .WithMessage(ExcelImportMessages.FileRequired)
            .Must(content => content.Length <= options.MaximumFileBytes)
            .WithMessage(ExcelImportMessages.FileTooLarge);
        RuleFor(command => command.Mapping.Date)
            .NotEmpty()
            .WithMessage(ExcelImportMessages.DateColumnRequired);
        RuleFor(command => command.Mapping.Amount)
            .NotEmpty()
            .WithMessage(ExcelImportMessages.AmountColumnRequired);
        RuleFor(command => command.Mapping.Direction)
            .NotEmpty()
            .WithMessage(ExcelImportMessages.DirectionColumnRequired);
        RuleFor(command => command.Mapping)
            .Must(HaveDistinctColumns)
            .WithMessage(ExcelImportMessages.ColumnsMustBeDistinct);
    }

    private static bool HaveDistinctColumns(ExcelColumnMapping mapping)
    {
        var columns = new[]
        {
            mapping.Date,
            mapping.Amount,
            mapping.Direction,
            mapping.Description,
            mapping.Category,
            mapping.ExternalId
        }.Where(column => !string.IsNullOrWhiteSpace(column)).Select(column => column!.Trim());
        return columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() == columns.Count();
    }
}
