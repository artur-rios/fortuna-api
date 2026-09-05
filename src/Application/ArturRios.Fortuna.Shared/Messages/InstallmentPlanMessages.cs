namespace ArturRios.Fortuna.Shared.Messages;

public static class InstallmentPlanMessages
{
    public const string RecordedSuccessfully = "Installment plan recorded successfully.";
    public const string RetrievedSuccessfully = "Installment plan retrieved successfully.";
    public const string DeletedSuccessfully = "Installment plan deleted successfully.";
    public const string RestoredSuccessfully = "Installment plan restored successfully.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string NotFound = "Installment plan not found.";
    public const string CreditCardNotFound = "Credit card not found.";
    public const string CategoryNotFound = "Category not found.";
    public const string CurrencyNotSupported = "Currency is not supported.";
    public const string ExchangeRateUnavailable =
        "No exchange rate is available for the purchase date.";
    public const string AmountTooSmall =
        "The total is too small to create positive installments.";
    public const string SettledStatementFrozen =
        "An installment belongs to a settled statement whose composition is frozen.";
    public const string RestoreRequiresSoftDeletion =
        "Installment plan must be soft-deleted before it can be restored.";
    public const string IdRequired = "Installment plan id is required.";
    public const string CreditCardIdRequired = "CreditCardId is required.";
    public const string CategoryIdRequired = "CategoryId is required.";
    public const string TotalAmountPositive = "TotalAmount must be greater than zero.";
    public const string TotalAmountPrecisionInvalid =
        "TotalAmount must contain at most 15 whole and 4 decimal digits.";
    public const string InstallmentCountMinimum = "InstallmentCount must be at least two.";
    public const string PurchasedOnRequired = "PurchasedOn is required.";
    public const string PurchasedOnTooFarInFuture =
        "PurchasedOn cannot be more than one day in the future.";
    public const string CurrencyCodeInvalid = "CurrencyCode must contain three letters.";
    public const string CounterpartyTooLong = "Counterparty cannot exceed 200 characters.";
    public const string OwnerImmutable = "OwnerId cannot be supplied; ownership is fixed.";
}
