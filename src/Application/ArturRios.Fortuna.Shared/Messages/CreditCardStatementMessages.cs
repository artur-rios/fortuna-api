namespace ArturRios.Fortuna.Shared.Messages;

public static class CreditCardStatementMessages
{
    public const string ClosedSuccessfully = "Credit card statement closed successfully.";
    public const string RetrievedSuccessfully = "Credit card statement retrieved successfully.";
    public const string ListedSuccessfully = "Credit card statements listed successfully.";
    public const string NotDue = "Credit card statement remains open until its closing date passes.";
    public const string NotFound = "Credit card statement not found.";
    public const string CreditCardNotFound = "Credit card not found.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string SettledStatementFrozen =
        "A settled credit card statement is frozen and cannot be recomputed.";
    public const string InvalidPageNumber = "PageNumber must be at least 1.";
    public const string InvalidPageSize = "PageSize must be at least 1.";
    public const string StatusInvalid = "Status must be Open, Closed or Settled.";
    public const string PeriodInvalid = "From must be on or before To.";
    public const string SortByUnsupported = "SortBy is not supported.";

    public static string UnsupportedFilter(string field) =>
        $"The filter '{field}' is not supported.";
}
