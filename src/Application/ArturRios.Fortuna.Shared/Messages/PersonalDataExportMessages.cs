namespace ArturRios.Fortuna.Shared.Messages;

public static class PersonalDataExportMessages
{
    public const string Accepted = "The personal data archive was queued successfully.";
    public const string RetrievedSuccessfully = "The personal data archive status was retrieved successfully.";
    public const string ProfileNotFound = "The user profile was not found.";
    public const string NotFound = "The personal data archive was not found.";
    public const string Expired = "The personal data archive has expired. Request a new archive.";
    public const string FileNotFound = "The personal data archive file is no longer available. Request a new archive.";
    public const string StorageUnavailable = "Personal data archive storage is unavailable.";
    public const string GenerationFailed = "The personal data archive could not be generated.";
}
