namespace ArturRios.Fortuna.Shared.Messages;

public static class FinancialAccountMessages
{
    public const string CreatedSuccessfully = "Financial account created successfully.";
    public const string DuplicateName = "A live financial account already uses this name.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string NameRequired = "Name is required.";
    public const string NameTooLong = "Name must not exceed 200 characters.";
    public const string InstitutionTooLong = "Institution must not exceed 200 characters.";
    public const string AccountTypeInvalid = "AccountType must be Checking, Savings, Cash or Other.";
    public const string CurrencyRequired = "CurrencyCode is required.";
    public const string CurrencyInvalid = "CurrencyCode must be a three-letter ISO 4217 code.";
    public const string CurrencyNotSupported = "CurrencyCode is not supported.";
    public const string OpeningBalancePrecisionInvalid =
        "OpeningBalance must contain at most 15 whole and 4 decimal digits.";

    public static string UnknownCurrency(string code) => $"Unknown currency code '{code}'.";
}
