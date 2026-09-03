namespace ArturRios.Fortuna.Shared.Messages;

public static class LocalAuthenticationMessages
{
    public const string AuthenticatedSuccessfully = "Local account authenticated successfully.";
    public const string InvalidCredentials = "The local account name or secret is invalid.";
    public const string PasswordResetUnavailable =
        "Password reset is not available. Recover the account with one of its recovery codes.";
}
