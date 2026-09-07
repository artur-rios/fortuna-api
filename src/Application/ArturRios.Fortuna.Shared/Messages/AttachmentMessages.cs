namespace ArturRios.Fortuna.Shared.Messages;

public static class AttachmentMessages
{
    public const string AttachedSuccessfully = "Document attached successfully.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string TransactionNotFound = "The transaction was not found.";
    public const string FileRequired = "A document file is required.";
    public const string FileNameRequired = "A document file name is required.";
    public const string FileNameTooLong = "A document file name cannot exceed 300 characters.";
    public const string StorageUnavailable = "Attachment storage is unavailable.";
    public const string PersistenceFailed = "The attachment metadata could not be saved.";
    public const string DownloadedSuccessfully = "Document downloaded successfully.";
    public const string AttachmentNotFound = "The attachment was not found.";
    public const string StoredObjectNotFound =
        "The stored object for the attachment was not found.";
    public const string DeletedSuccessfully = "Attachment deleted successfully.";
    public const string HardDeletedSuccessfully =
        "Attachment permanently deleted successfully.";
    public const string HardDeleteRequiresSoftDeletion =
        "The attachment must be soft-deleted before it can be permanently deleted.";

    public static string FileTooLarge(int maximumBytes) =>
        $"The document exceeds the configured maximum of {maximumBytes} bytes.";

    public static string ContentTypeNotAllowed(IEnumerable<string> allowedContentTypes) =>
        $"The document content type is not allowed. Allowed content types: {string.Join(", ", allowedContentTypes)}.";
}
