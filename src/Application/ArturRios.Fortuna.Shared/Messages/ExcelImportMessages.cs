namespace ArturRios.Fortuna.Shared.Messages;

public static class ExcelImportMessages
{
    public const string Accepted = "Excel import queued successfully.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string TargetNotFound = "The import target was not found.";
    public const string TargetDeleted = "The import target is deleted.";
    public const string FileRequired = "A workbook file is required.";
    public const string FileTooLarge = "The workbook exceeds the configured size limit.";
    public const string WorkbookInvalid = "The file is not a readable Excel workbook.";
    public const string TargetTypeInvalid = "TargetType must be 'account' or 'creditCard'.";
    public const string DateColumnRequired = "DateColumn is required.";
    public const string AmountColumnRequired = "AmountColumn is required.";
    public const string DirectionColumnRequired = "DirectionColumn is required.";
    public const string ColumnsMustBeDistinct = "Mapped columns must be distinct.";
    public const string RowDateInvalid = "The row date could not be parsed.";
    public const string RowAmountInvalid = "The row amount could not be parsed.";
    public const string RowDirectionInvalid = "The row direction could not be parsed.";
    public static string ColumnNotFound(string column) =>
        $"The mapped column '{column}' was not found in the workbook.";
}
