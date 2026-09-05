namespace ArturRios.Fortuna.Shared.Messages;

public static class InvestmentMessages
{
    public const string CreatedSuccessfully = "Investment created successfully.";
    public const string MovementRecordedSuccessfully =
        "Investment movement recorded successfully.";
    public const string DuplicateInstrument =
        "A live investment already uses this instrument name.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string NotFound = "Investment not found.";
    public const string FinancialAccountNotFound = "Financial account not found.";
    public const string InstrumentRequired = "Instrument is required.";
    public const string InstrumentTooLong = "Instrument must not exceed 200 characters.";
    public const string InstitutionTooLong = "Institution must not exceed 200 characters.";
    public const string InvestmentTypeInvalid =
        "InvestmentType must be FixedIncome, Equity, Fund or Other.";
    public const string CurrencyRequired = "CurrencyCode is required.";
    public const string CurrencyInvalid = "CurrencyCode must be a three-letter ISO 4217 code.";
    public const string CurrencyNotSupported = "CurrencyCode is not supported.";
    public const string InvestmentIdRequired = "InvestmentId is required.";
    public const string MovementTypeInvalid =
        "MovementType must be Contribution, Withdrawal, Yield or Fee.";
    public const string MovementAmountPositive = "Amount must be greater than zero.";
    public const string MovementAmountPrecisionInvalid =
        "Amount must contain at most 15 whole and 4 decimal digits.";
    public const string OccurredOnRequired = "OccurredOn is required.";
    public const string OccurredOnTooFarInFuture =
        "OccurredOn must not be more than one day in the future.";
    public const string FinancialAccountIdInvalid =
        "FinancialAccountId must be a non-empty identifier when supplied.";
    public const string FundingRequiresContribution =
        "FinancialAccountId can only fund a contribution.";
    public const string ExchangeRateUnavailable =
        "No exchange rate is available for the movement date.";
    public const string ConvertedAmountTooSmall =
        "The converted movement amount is below the investment currency precision.";

    public static string UnknownCurrency(string code) => $"Unknown currency code '{code}'.";
}
