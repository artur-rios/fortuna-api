namespace ArturRios.Fortuna.Shared.Messages;

public static class LocalAccountMessages
{
    public const string CreatedSuccessfully = "Local account created successfully.";
    public const string RecoveryWarning =
        "Save these recovery codes now. They are the only way back in if you lose your secret; losing all of them means losing the account.";
    public const string Disabled = "Local authentication is not available in this deployment.";
    public const string AlreadyExists = "A local account already exists on this installation.";
    public const string NameRequired = "DisplayName is required.";
    public const string NameTooLong = "DisplayName must not exceed 200 characters.";
    public const string SecretRequired = "Secret is required.";
    public const string SecretTooShort = "Secret must contain at least 8 characters.";
    public const string StorageModeInvalid = "StorageMode must be InMemory or OperatingSystem.";
    public const string CredentialStoreUnavailable =
        "The operating-system credential store is unavailable. Use the InMemory storage mode instead.";
}
