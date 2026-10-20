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
/// key it names among Heimdall's published ES256 keys alone. One that names no key gets none: those are
/// Heimdall's retired HS256 tokens, and the local tokens Fortuna once issued under Heimdall's issuer. A token
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
    private readonly SecurityKey _localKey;

    public FortunaSigningKeys(FortunaOptions options, HeimdallJwks jwks)
    {
        _options = options;
        _jwks = jwks;
        _localKey = new JwtKey("local", options.LocalTokenSecret).ToSecurityKey();
    }

    /// <summary>The keys that may verify <paramref name="token" />, by the issuer it names.</summary>
    public IEnumerable<SecurityKey> Resolve(SecurityToken token, string? keyId)
    {
        if (string.Equals(token.Issuer, _options.LocalTokenIssuer, StringComparison.Ordinal))
        {
            return [_localKey];
        }

        if (!string.Equals(token.Issuer, _options.AuthTokenIssuer, StringComparison.Ordinal) ||
            string.IsNullOrEmpty(keyId))
        {
            return [];
        }

        return _jwks.Keys.Where(key => key.Id == keyId).Select(key => key.ToSecurityKey()).ToList();
    }

    /// <summary>Whether <paramref name="key" /> may verify <paramref name="algorithm" />: only its own.</summary>
    public static bool IsAllowed(string algorithm, SecurityKey key) => key switch
    {
        ECDsaSecurityKey => algorithm == SecurityAlgorithms.EcdsaSha256,
        SymmetricSecurityKey => algorithm == SecurityAlgorithms.HmacSha256,
        _ => false
    };
}
