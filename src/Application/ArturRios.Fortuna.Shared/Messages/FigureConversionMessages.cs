namespace ArturRios.Fortuna.Shared.Messages;

public static class FigureConversionMessages
{
    public const string ConvertedSuccessfully = "Figure converted successfully.";
    public const string PartiallyConverted = "Figure returned with unconverted currency groups.";
    public const string DisplayCurrencyInvalid = "Display currency must be a three-letter code.";
    public const string FigureDateRequired = "Figure date is required.";
    public const string AmountsRequired = "Amounts are required.";
    public const string AmountCurrencyRequired = "Each amount must carry a currency.";
    public const string AmountCurrencyInvalid = "Each amount currency must be a three-letter code.";
    public const string AmountPrecisionInvalid = "Each amount must have at most 15 whole and 4 decimal digits.";
    public const string CurrencyNotSupported = "A supplied currency is not supported.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string RateUnavailable = "No exchange rate has ever been stored for this currency pair.";

    public static string UnknownCurrency(string code) => $"Unknown currency code '{code}'.";
}
