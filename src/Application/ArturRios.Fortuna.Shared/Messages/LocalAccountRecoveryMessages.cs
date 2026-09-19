namespace ArturRios.Fortuna.Shared.Messages;

public static class LocalAccountRecoveryMessages
{
    public const string RecoveredSuccessfully = "Local account recovered successfully.";
    public const string InvalidRecoveryCode = "The recovery code is invalid or has already been used.";
    public const string RecoveryCodesExhausted =
        "The account cannot be recovered because every recovery code has been used.";
    public const string NameRequired = "Name is required.";
    public const string NameTooLong = "Name must not exceed 200 characters.";
    public const string RecoveryCodeRequired = "RecoveryCode is required.";
    public const string RecoveryCodeFormatInvalid =
        "RecoveryCode must look like XXXX-XXXX, using letters and digits.";
    public const string NewSecretRequired = "NewSecret is required.";
    public const string NewSecretTooShort = "NewSecret must contain at least 8 characters.";
    public const string NewSecretTooLong = "NewSecret must not exceed 1024 characters.";
}
