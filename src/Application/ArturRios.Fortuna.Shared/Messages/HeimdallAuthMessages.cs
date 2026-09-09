namespace ArturRios.Fortuna.Shared.Messages;

public static class HeimdallAuthMessages
{
    public const string AuthenticatedSuccessfully = "Authentication succeeded.";
    public const string SignedOutSuccessfully = "Google session signed out successfully.";
    public const string AuthenticationRejected = "The supplied authentication credentials are invalid.";
    public const string TwoFactorRejected = "The supplied two-factor challenge or factor is invalid.";
    public const string ServiceUnavailable = "The identity service is temporarily unavailable.";
    public const string EmailRequired = "Email is required.";
    public const string EmailInvalid = "Email must be a valid address.";
    public const string PasswordRequired = "Password is required.";
    public const string GoogleIdTokenRequired = "Google ID token is required.";
    public const string ChallengeTokenRequired = "Challenge token is required.";
    public const string FactorRequired = "A two-factor code or recovery code is required.";
    public const string FactorAmbiguous = "Supply either a two-factor code or a recovery code, not both.";
}
