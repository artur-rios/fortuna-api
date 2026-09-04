namespace ArturRios.Fortuna.Shared.Messages;

public static class LocalRecoveryCodeRegenerationMessages
{
    public const string RegeneratedSuccessfully = "Recovery codes regenerated successfully.";
    public const string InvalidSecret = "The current secret is invalid.";
    public const string LocalAccountOnly = "Recovery codes are not available for this account.";
}
