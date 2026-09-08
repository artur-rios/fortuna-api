namespace ArturRios.Fortuna.Shared.Messages;

public static class DataExportMessages
{
    public const string CreatedSuccessfully = "The export was created successfully.";
    public const string Accepted = "The export was queued successfully.";
    public const string ProfileNotFound = "The user profile was not found.";
    public const string FormatUnsupported =
        "Format must be one of: csv, xlsx, pdf.";
    public const string LocaleInvalid = "Locale must be a specific culture name.";
}
