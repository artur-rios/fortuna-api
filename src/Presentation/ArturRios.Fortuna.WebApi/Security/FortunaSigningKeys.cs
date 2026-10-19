using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
/// The keys that may verify a bearer token, chosen by the issuer it names, and the one algorithm each key
/// verifies.
/// </summary>
/// <remarks>
/// <para>
/// <b>By issuer.</b> A token naming Heimdall's issuer (<c>FORTUNA_AUTH_TOKEN_ISSUER</c>) is verified with the
/// key it names among Heimdall's published ES256 keys, or, when it names none, with the legacy HS256 secret,
/// and only while the overlap is on (<see cref="FortunaOptions.AcceptsLegacyTokens" />). Heimdall's HS256
/// tokens, and the local tokens Fortuna issued under Heimdall's issuer, never carried a <c>kid</c>. A token
/// naming Fortuna's local issuer is verified with Fortuna's local secret alone. Any other issuer gets no key.
/// </para>
/// <para>
/// <b>By key.</b> <see cref="IsAllowed" /> lets an EC key verify ES256 and a secret verify HS256, and nothing
/// else, so a token's <c>alg</c> header never decides what kind of key checks it: an HS256 token keyed with
/// Heimdall's public key meets an EC key and is refused, and an HS384 or HS512 token meets a secret that
/// verifies HS256 only.
/// </para>
/// </remarks>
public sealed class FortunaSigningKeys
{
    private readonly FortunaOptions _options;
    private readonly HeimdallJwks _jwks;
    private readonly IReadOnlyList<SecurityKey> _legacyHeimdallKeys;
    private readonly SecurityKey _localKey;

    public FortunaSigningKeys(FortunaOptions options, HeimdallJwks jwks)
    {
        _options = options;
        _jwks = jwks;
        _legacyHeimdallKeys = LegacyHeimdallSecrets(options).Select(key => key.ToSecurityKey()).ToList();
        _localKey = new JwtKey("local", options.LocalTokenSecret).ToSecurityKey();
    }

    /// <summary>Whether Heimdall's HS256 tokens are still accepted: the overlap is on.</summary>
    public bool AcceptsLegacyHeimdallTokens => _options.AcceptsLegacyTokens;

    /// <summary>The keys that may verify <paramref name="token" />, by the issuer it names.</summary>
    public IEnumerable<SecurityKey> Resolve(SecurityToken token, string? keyId)
    {
        if (string.Equals(token.Issuer, _options.LocalTokenIssuer, StringComparison.Ordinal))
        {
            return [_localKey];
        }

        if (!string.Equals(token.Issuer, _options.AuthTokenIssuer, StringComparison.Ordinal))
        {
            return [];
        }

        return string.IsNullOrEmpty(keyId)
            ? _legacyHeimdallKeys
            : _jwks.Keys.Where(key => key.Id == keyId).Select(key => key.ToSecurityKey()).ToList();
    }

    /// <summary>Whether <paramref name="key" /> may verify <paramref name="algorithm" />: only its own.</summary>
    public static bool IsAllowed(string algorithm, SecurityKey key) => key switch
    {
        ECDsaSecurityKey => algorithm == SecurityAlgorithms.EcdsaSha256,
        SymmetricSecurityKey => algorithm == SecurityAlgorithms.HmacSha256,
        _ => false
    };

    private static IEnumerable<JwtKey> LegacyHeimdallSecrets(FortunaOptions options)
    {
        if (!options.AcceptsLegacyTokens)
        {
            yield break;
        }

        yield return new JwtKey("legacy-current", options.AuthTokenSecret!);

        if (!string.IsNullOrWhiteSpace(options.AuthPreviousTokenSecret) &&
            options.AuthPreviousTokenSecret != options.AuthTokenSecret)
        {
            yield return new JwtKey("legacy-previous", options.AuthPreviousTokenSecret);
        }
    }
}
