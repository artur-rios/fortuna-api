using System.Security.Cryptography;
using System.Text;
using ArturRios.Util.Hashing;

namespace ArturRios.Fortuna.Shared.Users;

/// <summary>
/// Derives and verifies the stored digest of a local-account recovery code.
/// </summary>
/// <remarks>
/// A recovery code carries only about 41 bits of entropy, so an unsalted fast digest can be
/// reversed offline in moments by anyone holding a database copy. Codes are therefore stored as
/// <c>[version][salt][Argon2id(code, salt)]</c>. Digests written before this format existed are
/// plain SHA-256 values (exactly 32 bytes) and keep verifying; being single-use, each one is
/// retired the moment it is redeemed, and regenerating the codes replaces all of them.
/// </remarks>
public static class LocalRecoveryCodeHash
{
    private const byte CurrentVersion = 1;
    private const int LegacyDigestLength = 32;
    private const int SaltLength = 16;

    // Deliberately lighter than the account-secret defaults: redeeming a code verifies it against
    // every unused code (ten by default), so the cost is paid once per stored code. These values
    // follow the OWASP Argon2id minimum (19 MiB, two iterations, one lane).
    private static readonly HashConfiguration Configuration = new(1, 2, 19 * 1024);

    /// <summary>Normalizes user input so case and surrounding whitespace do not matter.</summary>
    public static string Normalize(string? code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();

    public static byte[] Compute(string code)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var digest = Hash.EncodeWithSalt(Normalize(code), salt, Configuration);
        var stored = new byte[1 + salt.Length + digest.Length];
        stored[0] = CurrentVersion;
        salt.CopyTo(stored, 1);
        digest.CopyTo(stored, 1 + salt.Length);

        return stored;
    }

    public static bool Matches(string code, byte[] stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var normalized = Normalize(code);

        if (stored.Length == LegacyDigestLength)
        {
            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalized)),
                stored);
        }

        if (stored.Length <= 1 + SaltLength || stored[0] != CurrentVersion)
        {
            return false;
        }

        var salt = stored.AsSpan(1, SaltLength).ToArray();
        var digest = Hash.EncodeWithSalt(normalized, salt, Configuration);

        return CryptographicOperations.FixedTimeEquals(digest, stored.AsSpan(1 + SaltLength));
    }
}
