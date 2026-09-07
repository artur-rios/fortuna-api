namespace ArturRios.Fortuna.Shared.Messages;

public static class TransactionDrillDownMessages
{
    public const string AggregationRetrieved =
        "The next aggregation level was retrieved successfully.";
    public const string TransactionsRetrieved =
        "The transactions behind the aggregation were retrieved successfully.";
    public const string TransactionRetrieved =
        "The transaction behind the aggregation was retrieved successfully.";
    public const string ProfileNotFound = "The user profile was not found.";
    public const string KeyRequired = "Key is required.";
    public const string KeyTooLong = "Key cannot exceed 8192 characters.";
    public const string KeyInvalidOrExpired =
        "The drill-down key is malformed or expired; request the aggregation again.";
    public const string BucketNotFound = "The aggregation bucket was not found.";
    public const string DimensionAlreadyUsed =
        "The requested dimension is already present in the drill-down path.";
    public const string InvalidPageNumber = "PageNumber must be at least 1.";
    public const string InvalidPageSize = "PageSize must be at least 1.";
    public const string RecordsChanged =
        "Records changed after the chart was produced; totals may differ from the chart.";

    public static string UnknownDimension(string value, IEnumerable<string> supported) =>
        $"Dimension '{value}' is not supported. Supported dimensions: " +
        $"{string.Join(", ", supported.Order(StringComparer.OrdinalIgnoreCase))}.";
}
