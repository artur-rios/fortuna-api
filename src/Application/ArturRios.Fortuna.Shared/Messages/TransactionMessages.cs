namespace ArturRios.Fortuna.Shared.Messages;

public static class TransactionMessages
{
    public const string CardChargeCreatedSuccessfully =
        "Credit card charge recorded and assigned to its statement successfully.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string CreditCardNotFound = "Credit card not found.";
    public const string CreditCardIdRequired = "CreditCardId is required.";
    public const string AmountPositive = "Amount must be greater than zero.";
    public const string AmountPrecisionInvalid =
        "Amount must contain at most 15 whole and 4 decimal digits.";
    public const string OccurredOnRequired = "OccurredOn is required.";
}
