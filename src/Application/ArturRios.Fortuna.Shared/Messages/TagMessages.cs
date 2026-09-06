namespace ArturRios.Fortuna.Shared.Messages;

public static class TagMessages
{
    public const string CreatedSuccessfully = "Tag created successfully.";
    public const string UpdatedSuccessfully = "Tag updated successfully.";
    public const string DeletedSuccessfully = "Tag deleted successfully.";
    public const string ListedSuccessfully = "Tags retrieved successfully.";
    public const string AttachedSuccessfully = "Tag attached successfully.";
    public const string AlreadyAttached = "The tag was already attached.";
    public const string DetachedSuccessfully = "Tag detached successfully.";
    public const string AlreadyDetached = "The tag was already detached.";
    public const string NotFound = "Tag not found.";
    public const string AssignmentNotFound = "Tag or transaction not found.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string DuplicateName = "A live tag already uses this name.";
    public const string NameRequired = "Name is required.";
    public const string NameTooLong = "Name must not exceed 200 characters.";
    public const string TransactionIdInvalid = "TransactionId cannot be empty.";
    public const string TagIdInvalid = "TagId cannot be empty.";
    public const string MaximumExceeded =
        "The configured maximum number of tags per transaction was exceeded.";

    public static string MaximumAllowed(int maximum) =>
        $"A transaction can have at most {maximum} tags.";
}
