namespace ArturRios.Fortuna.Shared.Messages;

public static class ProcessingConsentMessages
{
    public const string RetrievedSuccessfully = "Processing consents retrieved successfully.";
    public const string GrantedSuccessfully = "Processing consent recorded successfully.";
    public const string WithdrawnSuccessfully = "Processing consent withdrawn successfully.";
    public const string ProfileNotFound = "The user profile was not found.";
    public const string UnknownPurpose = "The processing consent purpose is not recognized.";
    public const string VersionRequired = "The processing consent version is required.";
    public const string VersionNotCurrent = "The processing consent version is not current.";
    public const string NotFound = "The processing consent was not found.";
    public const string ExternalDataProcessingRequired =
        "Current consent for external-data-processing is required.";
}
