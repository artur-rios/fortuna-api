using System.Security.Claims;
using ArturRios.Fortuna.WebApi.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>What makes a bearer token a Fortuna local-account token.</summary>
/// <remarks>
/// <para>
/// A token under Fortuna's local issuer is local: only Fortuna's own key verifies tokens under it. The
/// <c>fortuna_local</c> claim counts in one case alone: the token names Heimdall's issuer and was verified
/// by the legacy shared secret while the overlap is on. Those are the local tokens Fortuna issued under
/// Heimdall's issuer, and only they carry the claim. An ES256 token from Heimdall is never local by claim,
/// whatever it carries.
/// </para>
/// <para>
/// Two steps enforce that. At validation, <see cref="OnTokenValidatedAsync" /> removes the claim from every
/// token not verified that way. The mapper then reads the claim only under a rule that
/// <see cref="AcceptsLegacyLocalClaim" />, so once the shared secret is gone it is ignored even if a token
/// still carries it.
/// </para>
/// </remarks>
/// <param name="LocalIssuer">
/// <c>FORTUNA_LOCAL_TOKEN_ISSUER</c>: a token under this issuer is a local-account token, whatever else it
/// claims.
/// </param>
/// <param name="AcceptsLegacyLocalClaim">
/// Whether a <c>fortuna_local</c> claim that survived validation still counts. It counts only while the
/// legacy shared secret is configured (<see cref="FortunaOptions.AcceptsLegacyTokens" />).
/// </param>
public sealed record LocalIdentityRule(string LocalIssuer, bool AcceptsLegacyLocalClaim)
{
    /// <summary>
    /// The default local issuer, with the legacy claim ignored. This fails closed: code that reads claims
    /// without the configured rule never treats a token under Heimdall's issuer as local.
    /// </summary>
    public static readonly LocalIdentityRule Default =
        new(FortunaOptions.DefaultLocalTokenIssuer, AcceptsLegacyLocalClaim: false);

    /// <summary>The rule the configuration sets: its local issuer, and the legacy claim only during the overlap.</summary>
    public static LocalIdentityRule From(FortunaOptions options) =>
        new(options.LocalTokenIssuer, options.AcceptsLegacyTokens);

    /// <summary>
    /// Whether <paramref name="validatedToken" /> may keep its <c>fortuna_local</c> claim. That holds only
    /// for an HS256 token under Heimdall's issuer while the overlap is on. Under that issuer, an HS256 token
    /// can only have been verified by the legacy shared secret: published EC keys verify ES256 alone
    /// (<see cref="FortunaSigningKeys.IsAllowed" />).
    /// </summary>
    public static bool KeepsLegacyLocalClaim(SecurityToken validatedToken, FortunaOptions options) =>
        options.AcceptsLegacyTokens &&
        validatedToken is JsonWebToken token &&
        string.Equals(token.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal) &&
        string.Equals(token.Issuer, options.AuthTokenIssuer, StringComparison.Ordinal);

    /// <summary>
    /// The <c>OnTokenValidated</c> step of the bearer scheme. It removes every <c>fortuna_local</c> claim
    /// unless <see cref="KeepsLegacyLocalClaim" /> holds.
    /// </summary>
    public static Task OnTokenValidatedAsync(TokenValidatedContext context, FortunaOptions options)
    {
        if (context.Principal is { } principal && !KeepsLegacyLocalClaim(context.SecurityToken, options))
        {
            RemoveLegacyLocalClaims(principal);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes every <c>fortuna_local</c> claim, in any letter case, from every identity of
    /// <paramref name="principal" />. The case is ignored here rather than left to
    /// <see cref="ClaimsIdentity.FindAll(string)" />, because the identity a JSON web token yields compares claim
    /// types ordinally. <see cref="ClaimsIdentity.RemoveClaim" /> throws when it cannot remove a claim, which
    /// fails the authentication rather than letting the claim through.
    /// </summary>
    public static void RemoveLegacyLocalClaims(ClaimsPrincipal principal)
    {
        foreach (var identity in principal.Identities)
        {
            var legacyClaims = identity.Claims
                .Where(claim => string.Equals(
                    claim.Type, FortunaIdentityMapper.LocalIdentityClaim, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var claim in legacyClaims)
            {
                identity.RemoveClaim(claim);
            }
        }
    }
}
