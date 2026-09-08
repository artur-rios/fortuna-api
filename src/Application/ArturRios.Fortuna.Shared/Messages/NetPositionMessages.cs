namespace ArturRios.Fortuna.Shared.Messages;

public static class NetPositionMessages
{
    public const string RetrievedSuccessfully = "The net position was retrieved successfully.";
    public const string PartiallyConverted =
        "The net position was returned by source currency because one or more rates were unavailable.";
    public const string ProfileNotFound = "The user profile was not found.";
    public const string DisplayCurrencyInvalid =
        "DisplayCurrencyCode must be a three-letter currency code.";
    public const string DisplayCurrencyUnsupported = "The display currency is not supported.";

    public static string UnknownCurrency(string code) =>
        $"Currency '{code}' is not present in the reference set.";
}
