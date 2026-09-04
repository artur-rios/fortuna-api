namespace ArturRios.Fortuna.Shared.Messages;

public static class InvestmentMessages
{
    public const string CreatedSuccessfully = "Investment created successfully.";
    public const string DuplicateInstrument =
        "A live investment already uses this instrument name.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string InstrumentRequired = "Instrument is required.";
    public const string InstrumentTooLong = "Instrument must not exceed 200 characters.";
    public const string InstitutionTooLong = "Institution must not exceed 200 characters.";
    public const string InvestmentTypeInvalid =
        "InvestmentType must be FixedIncome, Equity, Fund or Other.";
    public const string CurrencyRequired = "CurrencyCode is required.";
    public const string CurrencyInvalid = "CurrencyCode must be a three-letter ISO 4217 code.";
    public const string CurrencyNotSupported = "CurrencyCode is not supported.";

    public static string UnknownCurrency(string code) => $"Unknown currency code '{code}'.";
}
