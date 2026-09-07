namespace ArturRios.Fortuna.Shared.Messages;

public static class TableReportMessages
{
    public const string RetrievedSuccessfully = "The table was retrieved successfully.";
    public const string ProfileNotFound = "The user profile was not found.";
    public const string RecordSetRequired = "RecordSet is required.";
    public const string ColumnsRequired = "At least one column is required.";
    public const string ColumnsMustBeUnique = "Columns must not contain duplicates.";
    public const string InvalidPageNumber = "PageNumber must be at least 1.";
    public const string InvalidPageSize = "PageSize must be at least 1.";
    public const string FilterFieldRequired = "Every filter field is required.";
    public const string FilterOperatorRequired = "Every filter operator is required.";
    public const string FilterValueRequired = "Every filter value is required.";
    public const string SortFieldRequired = "Every sort field is required.";
    public const string DisplayCurrencyInvalid =
        "DisplayCurrencyCode must be a three-letter currency code.";
    public const string DisplayCurrencyUnsupported = "The display currency is not supported.";

    public static string UnknownRecordSet(string name, IEnumerable<string> supported) =>
        $"Record set '{name}' is not supported. Supported record sets: {Join(supported)}.";

    public static string UnknownColumn(string recordSet, string name) =>
        $"Column '{name}' is not supported by record set '{recordSet}'.";

    public static string UnknownFilterField(string recordSet, string name) =>
        $"Filter field '{name}' is not supported by record set '{recordSet}'.";

    public static string UnknownFilterOperator(string field, string value) =>
        $"Filter operator '{value}' is not supported for field '{field}'.";

    public static string InvalidFilterValue(string field, string value) =>
        $"Filter value '{value}' is invalid for field '{field}'.";

    public static string UnknownSortField(string recordSet, string name) =>
        $"Sort field '{name}' is not supported by record set '{recordSet}'.";

    public static string UnknownCurrency(string code) =>
        $"Currency '{code}' is not present in the reference set.";

    private static string Join(IEnumerable<string> values) =>
        string.Join(", ", values.Order(StringComparer.OrdinalIgnoreCase));
}
