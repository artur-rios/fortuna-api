namespace ArturRios.Fortuna.Shared.Messages;

public static class TransferMessages
{
    public const string RecordedSuccessfully = "Transfer recorded successfully.";
    public const string RetrievedSuccessfully = "Transfer retrieved successfully.";
    public const string DeletedSuccessfully = "Transfer deleted successfully.";
    public const string RestoredSuccessfully = "Transfer restored successfully.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string OriginFinancialAccountNotFound = "Origin financial account not found.";
    public const string DestinationFinancialAccountNotFound =
        "Destination financial account not found.";
    public const string DestinationStatementNotFound = "Destination statement not found.";
    public const string NotFound = "Transfer not found.";
    public const string AccountsMustDiffer =
        "Origin and destination financial accounts must be different.";
    public const string ExchangeRateUnavailable =
        "No exchange rate is available for the transfer date.";
    public const string ConvertedAmountTooSmall =
        "The converted amount is too small for the destination currency.";
    public const string StatementOpen = "An open statement cannot be settled.";
    public const string StatementAlreadySettled = "The statement is already settled.";
    public const string SettledStatementFrozen =
        "The transfer settled a statement whose composition is frozen.";
    public const string RestoreRequiresSoftDeletion =
        "Transfer must be soft-deleted before it can be restored.";
    public const string TransferIdRequired = "Transfer id is required.";
    public const string OriginFinancialAccountIdRequired =
        "OriginFinancialAccountId is required.";
    public const string ExactlyOneDestinationRequired =
        "Exactly one of DestinationFinancialAccountId or DestinationStatementId is required.";
    public const string AmountPositive = "Amount must be greater than zero.";
    public const string AmountPrecisionInvalid =
        "Amount must contain at most 15 whole and 4 decimal digits.";
    public const string OccurredOnRequired = "OccurredOn is required.";
    public const string OccurredOnTooFarInFuture =
        "OccurredOn cannot be more than one day in the future.";
    public const string OwnerImmutable = "OwnerId cannot be supplied; ownership is fixed.";
}
