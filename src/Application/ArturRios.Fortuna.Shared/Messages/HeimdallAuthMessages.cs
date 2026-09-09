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
    public const string PasswordRecoveryRequested = "If the address is registered, recovery instructions were sent.";
    public const string PasswordReset = "Password reset successfully.";
    public const string EmailVerified = "Email verified successfully.";
    public const string VerificationResent = "Verification message sent successfully.";
    public const string TwoFactorStatusReturned = "Two-factor status returned successfully.";
    public const string TwoFactorSetupStarted = "Two-factor setup started successfully.";
    public const string TwoFactorEnabled = "Two-factor authentication enabled successfully.";
    public const string TwoFactorDisabled = "Two-factor authentication disabled successfully.";
    public const string RecoveryCodesRegenerated = "Recovery codes regenerated successfully.";
    public const string RequestRejected = "The identity request is invalid.";
    public const string TwoFactorNotFound = "No active two-factor setup was found.";
    public const string TokenRequired = "Token is required.";
    public const string NewPasswordRequired = "New password is required.";
    public const string MethodsRequired = "At least one two-factor method is required.";
    public const string MethodInvalid = "Two-factor methods must be App or Email.";
    public const string ConfirmationCodeRequired = "An app or email confirmation code is required.";
}
