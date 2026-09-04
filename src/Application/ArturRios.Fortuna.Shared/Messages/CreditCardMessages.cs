namespace ArturRios.Fortuna.Shared.Messages;

public static class CreditCardMessages
{
    public const string CreatedSuccessfully = "Credit card created successfully.";
    public const string DuplicateName = "A live credit card already uses this name.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string NameRequired = "Name is required.";
    public const string NameTooLong = "Name must not exceed 200 characters.";
    public const string IssuerRequired = "Issuer is required.";
    public const string IssuerTooLong = "Issuer must not exceed 200 characters.";
    public const string CurrencyRequired = "CurrencyCode is required.";
    public const string CurrencyInvalid = "CurrencyCode must be a three-letter ISO 4217 code.";
    public const string CurrencyNotSupported = "CurrencyCode is not supported.";
    public const string CreditLimitPositive = "CreditLimit must be greater than zero.";
    public const string CreditLimitPrecisionInvalid =
        "CreditLimit must contain at most 15 whole and 4 decimal digits.";
    public const string ClosingDayInvalid = "ClosingDay must be between 1 and 31.";
    public const string DueDayInvalid = "DueDay must be between 1 and 31.";
    public const string LastFourDigitsInvalid = "LastFourDigits must contain exactly four numeric digits.";

    public static string UnknownCurrency(string code) => $"Unknown currency code '{code}'.";
}
