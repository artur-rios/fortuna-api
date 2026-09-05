namespace ArturRios.Fortuna.Shared.Messages;

public static class TransactionMessages
{
    public const string RecordedSuccessfully = "Transaction recorded successfully.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string CreditCardNotFound = "Credit card not found.";
    public const string FinancialAccountNotFound = "Financial account not found.";
    public const string CategoryNotFound = "Category not found.";
    public const string AmountPositive = "Amount must be greater than zero.";
    public const string AmountPrecisionInvalid =
        "Amount must contain at most 15 whole and 4 decimal digits.";
    public const string OccurredOnRequired = "OccurredOn is required.";
    public const string OccurredOnTooFarInFuture =
        "OccurredOn cannot be more than one day in the future.";
    public const string DirectionInvalid = "Direction must be Expense or Earning.";
    public const string ExactlyOneTargetRequired =
        "Exactly one of FinancialAccountId or CreditCardId is required.";
    public const string CategoryIdRequired = "CategoryId is required.";
    public const string CurrencyInvalid = "CurrencyCode must contain three characters.";
    public const string CurrencyNotSupported = "CurrencyCode is not supported.";
    public const string ExchangeRateUnavailable =
        "No exchange rate is available for the transaction date.";
    public const string ConvertedAmountTooSmall =
        "The converted amount is too small for the target currency.";
    public const string DescriptionTooLong = "Description cannot exceed 500 characters.";
    public const string CounterpartyTooLong = "Counterparty cannot exceed 200 characters.";
    public const string TooManyTags = "A transaction cannot contain more than 50 tags.";
    public const string TagRequired = "Tag names cannot be empty.";
    public const string TagTooLong = "Tag names cannot exceed 200 characters.";
    public const string OwnerImmutable = "OwnerId cannot be supplied; ownership is fixed.";
}
