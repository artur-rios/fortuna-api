using ArturRios.Fortuna.WebApi.Configuration;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>What makes a bearer token a Fortuna local-account token.</summary>
/// <param name="LocalIssuer">
/// <c>FORTUNA_LOCAL_TOKEN_ISSUER</c>: a token under this issuer is a local-account token, whatever else it
/// claims. Only Fortuna's own key verifies tokens under it.
/// </param>
/// <param name="AcceptsLegacyLocalClaim">
/// Whether the <c>fortuna_local</c> claim still counts. It does only while the legacy shared secret is
/// configured (<see cref="FortunaOptions.AcceptsLegacyTokens" />): local tokens Fortuna issued under
/// Heimdall's issuer were signed with that secret and carry the claim, but once the secret is gone no token
/// under Heimdall's issuer can be a local one.
/// </param>
public sealed record LocalIdentityRule(string LocalIssuer, bool AcceptsLegacyLocalClaim)
{
    /// <summary>The default local issuer, with the legacy claim honoured.</summary>
    public static readonly LocalIdentityRule Default =
        new(FortunaOptions.DefaultLocalTokenIssuer, AcceptsLegacyLocalClaim: true);
}
