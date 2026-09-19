namespace ArturRios.Fortuna.Command.Input.Validation;

/// <summary>Bounds shared by every local-account command validator.</summary>
internal static class LocalAccountInputLimits
{
    public const int NameMaximumLength = 200;
    public const int SecretMinimumLength = 8;

    // Secrets are hashed with Argon2id; bounding them keeps an anonymous request from handing the
    // hasher arbitrarily large input.
    public const int SecretMaximumLength = 1024;

    /// <summary>Two groups of four letters or digits, as issued; case is ignored.</summary>
    public const string RecoveryCodePattern = "^[A-Za-z0-9]{4}-[A-Za-z0-9]{4}$";
}
