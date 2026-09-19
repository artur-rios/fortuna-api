namespace ArturRios.Fortuna.Shared.Messages;

public static class LocalAuthenticationMessages
{
    public const string AuthenticatedSuccessfully = "Local account authenticated successfully.";
    public const string InvalidCredentials = "The local account name or secret is invalid.";
    public const string NameRequired = "Name is required.";
    public const string NameTooLong = "Name must not exceed 200 characters.";
    public const string SecretRequired = "Secret is required.";
    public const string SecretTooLong = "Secret must not exceed 1024 characters.";
    public const string PasswordResetUnavailable =
        "Password reset is not available. Recover the account with one of its recovery codes.";
}
