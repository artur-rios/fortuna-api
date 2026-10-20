using ArturRios.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
/// Before a bearer token is validated, makes sure the Heimdall key it names is known, fetching Heimdall's
/// key set when it is not (at most once a minute; see <see cref="HeimdallJwks" />).
/// </summary>
/// <remarks>
/// <para>
/// Only an ES256 token under Heimdall's issuer that names a <c>kid</c> Fortuna does not know leads to a
/// fetch. Heimdall's retired HS256 tokens carry no <c>kid</c> and are refused, and local tokens are verified
/// with Fortuna's own secret, so neither ever spends the minute's fetch or logs its warning; a non-ES256
/// token naming a <c>kid</c> could not be verified by a published key anyway.
/// </para>
/// <para>
/// The token is read unverified, and only to decide whether to fetch: validation that follows checks the
/// signature, issuer, audience and lifetime as for any other token, and its <c>alg</c> header never
/// chooses the key that verifies it (see <see cref="FortunaSigningKeys" />).
/// </para>
/// </remarks>
public static class HeimdallKeyRefresh
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>The <c>OnMessageReceived</c> step of the bearer scheme.</summary>
    public static async Task BeforeValidationAsync(
        MessageReceivedContext context,
        string heimdallIssuer,
        HeimdallJwks jwks)
    {
        if (KeyIdToKnow(context.Request.Headers.Authorization.ToString(), heimdallIssuer) is { } keyId &&
            jwks.Keys.All(key => key.Id != keyId))
        {
            await jwks.EnsureKnownAsync(keyId, context.HttpContext.RequestAborted);
        }
    }

    /// <summary>
    /// The <c>kid</c> of a readable ES256 bearer token that names Heimdall's issuer, or <see langword="null" />.
    /// </summary>
    public static string? KeyIdToKnow(string? authorization, string heimdallIssuer)
    {
        if (authorization is null || !authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var token = new JsonWebToken(authorization[BearerPrefix.Length..].Trim());

            return string.Equals(token.Issuer, heimdallIssuer, StringComparison.Ordinal) &&
                   string.Equals(token.Alg, JwtAlgorithms.ES256, StringComparison.Ordinal) &&
                   !string.IsNullOrEmpty(token.Kid)
                ? token.Kid
                : null;
        }
        catch (Exception)
        {
            // Unreadable tokens are refused by validation itself; there is nothing to fetch for them.
            return null;
        }
    }
}
