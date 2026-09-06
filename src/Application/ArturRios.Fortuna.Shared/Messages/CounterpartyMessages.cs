namespace ArturRios.Fortuna.Shared.Messages;

public static class CounterpartyMessages
{
    public const string CreatedSuccessfully = "Counterparty created successfully.";
    public const string ReusedSuccessfully = "The existing counterparty was reused.";
    public const string UpdatedSuccessfully = "Counterparty updated successfully.";
    public const string DeletedSuccessfully = "Counterparty deleted successfully.";
    public const string MergedSuccessfully = "Counterparties merged successfully.";
    public const string ListedSuccessfully = "Counterparties retrieved successfully.";
    public const string SuggestedSuccessfully = "Category suggestion retrieved successfully.";
    public const string NoSuggestion = "No prior category was found for this counterparty.";
    public const string NotFound = "Counterparty not found.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string DuplicateName = "A live counterparty already uses this name.";
    public const string SameCounterparty = "A counterparty cannot be merged into itself.";
    public const string NameRequired = "Name is required.";
    public const string NameTooLong = "Name must not exceed 200 characters.";
    public const string TargetIdInvalid = "TargetId cannot be empty.";
}
