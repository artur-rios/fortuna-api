namespace ArturRios.Fortuna.Shared.Messages;

public static class CategoryMessages
{
    public const string CreatedSuccessfully = "Category created successfully.";
    public const string UpdatedSuccessfully = "Category updated successfully.";
    public const string TreeRetrievedSuccessfully = "Category tree retrieved successfully.";
    public const string RetrievedSuccessfully = "Category retrieved successfully.";
    public const string NotFound = "Category not found.";
    public const string DefaultSetAvailable =
        "No categories were found. A default category set can be seeded.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string ParentNotFound = "Parent category not found.";
    public const string DuplicateSiblingName =
        "A live sibling category already uses this name.";
    public const string CycleDetected =
        "The category placement would create or extend a cycle.";
    public const string NameRequired = "Name is required.";
    public const string NameTooLong = "Name must not exceed 200 characters.";
    public const string ParentIdInvalid = "ParentId cannot be empty.";
}
