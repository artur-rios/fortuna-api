namespace ArturRios.Fortuna.Shared.Messages;

public static class TransactionAggregationMessages
{
    public const string RetrievedSuccessfully = "The transaction aggregation was retrieved successfully.";
    public const string ProfileNotFound = "The user profile was not found.";
    public const string DimensionRequired = "Dimension is required.";
    public const string GranularityRequired =
        "Granularity is required when aggregating by period.";
    public const string FromRequired = "From is required.";
    public const string ToRequired = "To is required.";
    public const string DateRangeInvalid = "From must be on or before To.";
    public const string FinancialAccountIdInvalid =
        "FinancialAccountId must be a non-empty identifier when supplied.";
    public const string CreditCardIdInvalid =
        "CreditCardId must be a non-empty identifier when supplied.";
    public const string CategoryIdInvalid =
        "CategoryId must be a non-empty identifier when supplied.";
    public const string TagIdInvalid = "TagId must be a non-empty identifier when supplied.";
    public const string CounterpartyIdInvalid =
        "CounterpartyId must be a non-empty identifier when supplied.";
    public const string DirectionInvalid = "Direction is not supported.";
    public const string MinimumAmountInvalid = "MinimumAmount cannot be negative.";
    public const string MaximumAmountInvalid = "MaximumAmount cannot be negative.";
    public const string AmountPrecisionInvalid =
        "Amounts must fit numeric(19,4).";
    public const string AmountRangeInvalid =
        "MinimumAmount must be less than or equal to MaximumAmount.";
    public const string TextTooLong = "Text cannot exceed 500 characters.";
    public const string DisplayCurrencyInvalid =
        "DisplayCurrencyCode must be a three-letter currency code.";
    public const string DisplayCurrencyUnsupported = "The display currency is not supported.";

    public static string UnknownDimension(string value, IEnumerable<string> supported) =>
        $"Dimension '{value}' is not supported. Supported dimensions: {Join(supported)}.";

    public static string UnknownGranularity(string value, IEnumerable<string> supported) =>
        $"Granularity '{value}' is not supported. Supported granularities: {Join(supported)}.";

    public static string MaximumSpanExceeded(int days) =>
        $"The maximum aggregation range is {days} days.";

    public static string UnknownCurrency(string code) =>
        $"Currency '{code}' is not present in the reference set.";

    private static string Join(IEnumerable<string> values) =>
        string.Join(", ", values.Order(StringComparer.OrdinalIgnoreCase));
}
