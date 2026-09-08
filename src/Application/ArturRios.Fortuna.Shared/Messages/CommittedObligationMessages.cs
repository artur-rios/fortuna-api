namespace ArturRios.Fortuna.Shared.Messages;

public static class CommittedObligationMessages
{
    public const string RetrievedSuccessfully =
        "The committed obligations were retrieved successfully.";
    public const string PartiallyConverted =
        "The committed obligations include source amounts because one or more rates were unavailable.";
    public const string ProfileNotFound = "The user profile was not found.";
    public const string HorizonRequired = "HorizonDays must be greater than zero.";
    public const string DisplayCurrencyInvalid =
        "DisplayCurrencyCode must be a three-letter currency code.";
    public const string DisplayCurrencyUnsupported = "The display currency is not supported.";

    public static string HorizonMaximum(int maximum) =>
        $"HorizonDays cannot exceed the configured maximum of {maximum} days.";
}
