namespace ArturRios.Fortuna.Shared.Messages;

public static class LocalAccountRecoveryMessages
{
    public const string RecoveredSuccessfully = "Local account recovered successfully.";
    public const string InvalidRecoveryCode = "The recovery code is invalid or has already been used.";
    public const string RecoveryCodesExhausted =
        "The account cannot be recovered because every recovery code has been used.";
    public const string NewSecretRequired = "NewSecret is required.";
    public const string NewSecretTooShort = "NewSecret must contain at least 8 characters.";
}
