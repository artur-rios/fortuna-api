namespace ArturRios.Fortuna.Shared.Messages;

public static class ConnectionMessages
{
    public const string CreatedSuccessfully = "Connection created successfully.";
    public const string Duplicate = "A connection already exists for this reference.";
    public const string InvalidReference = "The connection reference is not valid at Pluggy.";
    public const string SourceUnavailable = "Pluggy is temporarily unavailable.";
    public const string SourceNotAvailable = "Pluggy is not available in this deployment.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string DataSourceRequired = "DataSource is required.";
    public const string DataSourceInvalid = "DataSource must be 'pluggy'.";
    public const string ExternalReferenceRequired = "ExternalReference is required.";
    public const string ExternalReferenceInvalid = "ExternalReference must be a Pluggy item identifier.";
    public const string BankCredentialRejected =
        "Bank credentials and additional request fields are not accepted.";
}
