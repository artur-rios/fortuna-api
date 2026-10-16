using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// What makes a token a local-account token: Fortuna's own issuer, and — only while the legacy shared
/// secret is configured — the fortuna_local claim that local tokens issued under Heimdall's issuer carry.
/// </summary>
public sealed class LocalIdentityRuleTests
{
    private static Dictionary<string, string> Claims(string issuer, bool localClaim)
    {
        var claims = new FortunaIdentityMapper().ToClaims(
            new FortunaIdentity(Guid.NewGuid(), (int)HeimdallRoles.User, null, []) { IsLocal = localClaim });
        claims[FortunaIdentityMapper.IssuerClaim] = issuer;

        return claims;
    }

    private static bool IsLocal(LocalIdentityRule rule, Dictionary<string, string> claims) =>
        Assert.IsType<FortunaIdentity>(new FortunaIdentityMapper(rule).FromClaims(claims)).IsLocal;

    [UnitFact]
    public void GivenTheLocalIssuer_WhenMapped_ThenTheActorIsLocal()
    {
        Assert.True(IsLocal(new LocalIdentityRule("fortuna-local", AcceptsLegacyLocalClaim: false), Claims("fortuna-local", localClaim: false)));
    }

    [UnitFact]
    public void GivenHeimdallsIssuerWithTheLegacyClaim_WhenTheOverlapLasts_ThenTheActorIsLocal()
    {
        Assert.True(IsLocal(new LocalIdentityRule("fortuna-local", AcceptsLegacyLocalClaim: true), Claims("heimdall-tests", localClaim: true)));
    }

    [UnitFact]
    public void GivenHeimdallsIssuerWithTheLegacyClaim_WhenTheOverlapHasEnded_ThenTheActorIsNotLocal()
    {
        Assert.False(IsLocal(new LocalIdentityRule("fortuna-local", AcceptsLegacyLocalClaim: false), Claims("heimdall-tests", localClaim: true)));
    }

    [UnitFact]
    public void GivenHeimdallsIssuerWithoutTheClaim_WhenMapped_ThenTheActorIsNotLocal()
    {
        Assert.False(IsLocal(new LocalIdentityRule("fortuna-local", AcceptsLegacyLocalClaim: true), Claims("heimdall-tests", localClaim: false)));
    }

    [UnitFact]
    public void GivenAnIssuerDifferingFromTheLocalOneOnlyInCase_WhenMapped_ThenTheActorIsNotLocal()
    {
        Assert.False(IsLocal(new LocalIdentityRule("fortuna-local", AcceptsLegacyLocalClaim: false), Claims("Fortuna-Local", localClaim: false)));
    }
}
