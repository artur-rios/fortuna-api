namespace ArturRios.Fortuna.Shared.Messages;

public static class AuditEntryMessages
{
    public const string RetrievedSuccessfully = "Audit entries retrieved successfully.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string InvalidPageNumber = "Page number must be at least 1.";
    public const string InvalidPageSize = "Page size must be between 1 and 100.";
    public const string EntityTypeTooLong = "Entity type must not exceed 100 characters.";
    public const string OperationTooLong = "Operation must not exceed 150 characters.";
    public const string OutcomeInvalid = "Outcome must be Succeeded or Refused.";
    public const string PeriodInvalid = "The period start must not be later than its end.";
}
