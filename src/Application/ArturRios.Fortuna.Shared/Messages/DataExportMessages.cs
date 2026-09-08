namespace ArturRios.Fortuna.Shared.Messages;

public static class DataExportMessages
{
    public const string CreatedSuccessfully = "The export was created successfully.";
    public const string Accepted = "The export was queued successfully.";
    public const string RetrievedSuccessfully = "The export was retrieved successfully.";
    public const string ProfileNotFound = "The user profile was not found.";
    public const string NotFound = "The export was not found.";
    public const string Expired =
        "The export file has expired and was removed. Request a new export.";
    public const string FileNotFound =
        "The export file is no longer available. Request a new export.";
    public const string StorageUnavailable = "Export storage is unavailable.";
    public const string GenerationFailed = "The export could not be generated.";
    public const string FormatUnsupported =
        "Format must be one of: csv, xlsx, pdf.";
    public const string LocaleInvalid = "Locale must be a specific culture name.";
}
