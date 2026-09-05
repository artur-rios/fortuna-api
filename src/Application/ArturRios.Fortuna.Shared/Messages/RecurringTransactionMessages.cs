namespace ArturRios.Fortuna.Shared.Messages;

public static class RecurringTransactionMessages
{
    public const string RecordedSuccessfully = "Recurring transaction defined successfully.";
    public const string RetrievedSuccessfully = "Recurring transaction retrieved successfully.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string FinancialAccountNotFound = "Financial account not found.";
    public const string CreditCardNotFound = "Credit card not found.";
    public const string CategoryNotFound = "Category not found.";
    public const string NotFound = "Recurring transaction not found.";
    public const string ExactlyOneTargetRequired =
        "Exactly one of FinancialAccountId or CreditCardId is required.";
    public const string CategoryIdRequired = "CategoryId is required.";
    public const string DirectionInvalid = "Direction must be Expense or Earning.";
    public const string AmountPositive = "Amount must be greater than zero.";
    public const string AmountPrecisionInvalid =
        "Amount must contain at most 15 whole and 4 decimal digits.";
    public const string FrequencyInvalid =
        "Frequency must be Weekly, Monthly, Quarterly, or Yearly.";
    public const string StartsOnRequired = "StartsOn is required.";
    public const string DateRangeInvalid = "EndsOn cannot be earlier than StartsOn.";
    public const string DescriptionTooLong = "Description cannot exceed 500 characters.";
    public const string CounterpartyTooLong = "Counterparty cannot exceed 200 characters.";
    public const string OwnerImmutable = "OwnerId cannot be supplied; ownership is fixed.";
    public const string IdRequired = "Recurring transaction id is required.";
}
