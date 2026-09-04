namespace ArturRios.Fortuna.Shared.Messages;

public static class ManualExchangeRateMessages
{
    public const string BaseCurrencyRequired = "Base currency is required.";
    public const string BaseCurrencyInvalid = "Base currency must be a three-letter ISO 4217 code.";
    public const string QuoteCurrencyRequired = "Quote currency is required.";
    public const string QuoteCurrencyInvalid = "Quote currency must be a three-letter ISO 4217 code.";
    public const string RateMustBePositive = "Rate must be greater than zero.";
    public const string RatePrecisionInvalid = "Rate must contain at most 11 whole digits and 8 decimal places.";
    public const string RateDateRequired = "Rate date is required.";
    public const string CurrenciesMustDiffer = "Base and quote currencies must differ.";
    public const string CurrencyNotSupported = "A supplied currency is not supported.";
    public const string RecordedSuccessfully = "Manual exchange rate recorded and now takes precedence for the pair and date.";
    public const string ReplacedSuccessfully = "Manual exchange rate replaced and continues to take precedence for the pair and date.";

    public static string UnknownCurrency(string code) => $"Unknown currency code '{code}'.";
}
