namespace ArturRios.Fortuna.Shared.Messages;

public static class ImportJobMessages
{
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string NotFound = "The import job was not found.";
    public const string RetrievedSuccessfully = "Import job retrieved successfully.";
    public const string ListedSuccessfully = "Import jobs listed successfully.";
    public const string RecordsListedSuccessfully = "Import job records listed successfully.";
    public const string RetryAccepted = "Import job retry queued successfully.";
    public const string RetryRequiresFailedJob = "Only a failed import job can be retried.";
    public const string SourceFileNotRetained =
        "The source file is no longer retained. Upload the file again to create a new import job.";
    public const string InvalidPageNumber = "PageNumber must be greater than or equal to 1.";
    public const string InvalidPageSize = "PageSize must be greater than or equal to 1.";
    public const string SourceTypeInvalid = "SourceType is invalid.";
    public const string StatusInvalid = "Status is invalid.";
    public const string SortByUnsupported = "SortBy is not supported.";
    public static string UnsupportedFilter(string field) =>
        $"Query parameter '{field}' is not supported.";
}
