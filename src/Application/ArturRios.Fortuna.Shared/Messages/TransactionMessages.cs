namespace ArturRios.Fortuna.Shared.Messages;

public static class TransactionMessages
{
    public const string RecordedSuccessfully = "Transaction recorded successfully.";
    public const string UpdatedSuccessfully = "Transaction updated successfully.";
    public const string DeletedSuccessfully = "Transaction deleted successfully.";
    public const string RestoredSuccessfully = "Transaction restored successfully.";
    public const string HardDeletedSuccessfully =
        "Transaction permanently deleted successfully.";
    public const string RetrievedSuccessfully = "Transaction retrieved successfully.";
    public const string ListedSuccessfully = "Transactions retrieved successfully.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string NotFound = "Transaction not found.";
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
    public const string TransactionTargetImmutable =
        "FinancialAccountId and CreditCardId cannot be supplied; the transaction target is fixed.";
    public const string TransactionCurrencyImmutable =
        "CurrencyCode cannot be supplied; the transaction currency is fixed by its target.";
    public const string SettledStatementFrozen =
        "The transaction belongs to a settled statement whose composition is frozen.";
    public const string TransferFieldsRestricted =
        "A transfer leg permits changes only to Description, CategoryId, and Tags.";
    public const string RestoreRequiresSoftDeletion =
        "Transaction must be soft-deleted before it can be restored.";
    public const string HardDeleteRequiresSoftDeletion =
        "Transaction must be soft-deleted before permanent deletion.";
    public const string TransactionIdRequired = "Transaction id is required.";
    public const string InvalidPageNumber = "PageNumber must be at least 1.";
    public const string InvalidPageSize = "PageSize must be at least 1.";
    public const string DateRangeInvalid = "From cannot be later than To.";
    public const string FinancialAccountIdInvalid = "FinancialAccountId cannot be empty.";
    public const string CreditCardIdInvalid = "CreditCardId cannot be empty.";
    public const string CategoryFilterIdInvalid = "CategoryId cannot be empty.";
    public const string TagIdInvalid = "TagId cannot be empty.";
    public const string CounterpartyIdInvalid = "CounterpartyId cannot be empty.";
    public const string MinimumAmountInvalid = "MinimumAmount cannot be negative.";
    public const string MaximumAmountInvalid = "MaximumAmount cannot be negative.";
    public const string AmountRangeInvalid =
        "MinimumAmount cannot be greater than MaximumAmount.";
    public const string SearchTextTooLong = "Text cannot exceed 500 characters.";
    public const string DisplayCurrencyInvalid =
        "DisplayCurrencyCode must contain three characters.";
    public const string SortByUnsupported = "SortBy is not supported.";

    public static string UnsupportedFilter(string filter) => $"Filter '{filter}' is not supported.";

    public static string UnknownCurrency(string code) => $"Unknown currency code '{code}'.";
}
