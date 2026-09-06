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
    public const string RetrievedSuccessfully = "Connection retrieved successfully.";
    public const string ListedSuccessfully = "Connections listed successfully.";
    public const string ReauthenticatedSuccessfully = "Connection reauthenticated successfully.";
    public const string NotFound = "The connection was not found.";
    public const string ReauthenticationNotRequired =
        "The connection does not require reauthentication.";
    public const string RequiresReauthentication = "The connection requires reauthentication.";
    public const string Revoked = "A revoked connection cannot be reauthenticated.";
    public const string DuplicateReference =
        "Another connection already uses this reference.";
    public const string InvalidPageNumber = "PageNumber must be greater than or equal to 1.";
    public const string InvalidPageSize = "PageSize must be greater than or equal to 1.";
    public const string SortByUnsupported = "SortBy is not supported.";
    public const string DataSourceTypeInvalid = "DataSourceType is invalid.";
    public const string StatusInvalid = "Status is invalid.";
    public static string UnsupportedFilter(string field) =>
        $"Query parameter '{field}' is not supported.";
}
