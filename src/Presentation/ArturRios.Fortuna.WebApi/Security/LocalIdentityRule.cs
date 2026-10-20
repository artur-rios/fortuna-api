using System.Security.Claims;
using ArturRios.Fortuna.WebApi.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>What makes a bearer token a Fortuna local-account token.</summary>
/// <remarks>
/// <para>
/// A token under Fortuna's local issuer is local: only Fortuna's own key verifies tokens under it. The
/// <c>fortuna_local</c> claim never makes a token local. Fortuna still writes it on the local tokens it
/// issues, because fortuna-ui reads it, but a token under any other issuer, Heimdall's included, is never
/// local by claim, whatever it carries.
/// </para>
/// <para>
/// Two steps enforce that. At validation, <see cref="OnTokenValidatedAsync" /> removes the claim from every
/// token not under the local issuer. The mapper then decides by the issuer alone
/// (<see cref="FortunaIdentityMapper" />).
/// </para>
/// </remarks>
/// <param name="LocalIssuer">
/// <c>FORTUNA_LOCAL_TOKEN_ISSUER</c>: a token under this issuer is a local-account token, whatever else it
/// claims.
/// </param>
public sealed record LocalIdentityRule(string LocalIssuer)
{
    /// <summary>The default local issuer, for code that reads claims without the configured rule.</summary>
    public static readonly LocalIdentityRule Default = new(FortunaOptions.DefaultLocalTokenIssuer);

    /// <summary>The rule the configuration sets: its local issuer.</summary>
    public static LocalIdentityRule From(FortunaOptions options) => new(options.LocalTokenIssuer);

    /// <summary>
    /// The <c>OnTokenValidated</c> step of the bearer scheme. It removes every <c>fortuna_local</c> claim
    /// unless the validated token names the local issuer.
    /// </summary>
    public static Task OnTokenValidatedAsync(TokenValidatedContext context, FortunaOptions options)
    {
        if (context.Principal is { } principal &&
            !string.Equals(context.SecurityToken?.Issuer, options.LocalTokenIssuer, StringComparison.Ordinal))
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
