namespace ArturRios.Fortuna.Shared.Messages;

public static class GoalMessages
{
    public const string CreatedSuccessfully = "Goal created successfully.";
    public const string UpdatedSuccessfully = "Goal updated successfully.";
    public const string DeletedSuccessfully = "Goal deleted successfully.";
    public const string RetrievedSuccessfully = "Goal retrieved successfully.";
    public const string ProgressRetrievedSuccessfully =
        "Goal progress retrieved successfully.";
    public const string ListedSuccessfully = "Goals retrieved successfully.";
    public const string NotFound = "Goal not found.";
    public const string ResourceNotFound =
        "One or more linked accounts or investments were not found.";
    public const string CurrencyNotSupported = "The currency is not supported.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string NameRequired = "Name is required.";
    public const string NameTooLong = "Name cannot exceed 200 characters.";
    public const string TargetAmountMustBePositive =
        "TargetAmount must be greater than zero.";
    public const string CurrencyRequired = "CurrencyCode is required.";
    public const string CurrencyInvalid = "CurrencyCode must contain three characters.";
    public const string TargetDateRequired = "TargetDate is required.";
    public const string TargetDateMustBeFuture = "TargetDate must be in the future.";
    public const string ResourcesRequired =
        "At least one account or investment is required.";
    public const string ResourceIdInvalid = "Linked identifiers cannot be empty.";
    public const string ResourceDeleted = "The linked resource is soft-deleted.";
}
