namespace ArturRios.Fortuna.Shared.Messages;

public static class CashFlowProjectionMessages
{
    public const string RetrievedSuccessfully = "The cash-flow projection was retrieved successfully.";
    public const string ProfileNotFound = "The user profile was not found.";
    public const string HorizonRequired = "HorizonDays must be greater than zero.";
    public const string DisplayCurrencyInvalid =
        "DisplayCurrencyCode must be a three-letter currency code.";
    public const string DisplayCurrencyUnsupported = "The display currency is not supported.";
    public const string ExchangeRateUnavailable =
        "An exchange rate required by the projection is unavailable.";
    public const string NoProjectionInputs =
        "No recurring transactions or committed obligations fall within the horizon.";
    public const string InsufficientHistory =
        "The estimated component was omitted because fewer than 30 days of history are available.";

    public static string HorizonMaximum(int maximum) =>
        $"HorizonDays cannot exceed the configured maximum of {maximum} days.";
}
