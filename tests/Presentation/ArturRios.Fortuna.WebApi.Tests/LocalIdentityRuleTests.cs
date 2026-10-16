using System.Security.Claims;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// What makes a token a local-account token: Fortuna's own issuer, and the fortuna_local claim of a token
/// under Heimdall's issuer only when the legacy shared secret verified it during the overlap.
/// </summary>
public sealed class LocalIdentityRuleTests
{
    private const string LocalIssuer = "fortuna-local";

    private static readonly JwtKey HeimdallKey = JwksTestSupport.NewKey();

    private static Dictionary<string, string> Claims(string issuer, bool localClaim)
    {
        var claims = IdentityClaims(localClaim);
        claims[FortunaIdentityMapper.IssuerClaim] = issuer;

        return claims;
    }

    private static Dictionary<string, string> IdentityClaims(bool localClaim) =>
        new FortunaIdentityMapper().ToClaims(
            new FortunaIdentity(Guid.NewGuid(), (int)HeimdallRoles.User, null, []) { IsLocal = localClaim });

    private static bool IsLocal(LocalIdentityRule rule, Dictionary<string, string> claims) =>
        Assert.IsType<FortunaIdentity>(new FortunaIdentityMapper(rule).FromClaims(claims)).IsLocal;

    private static string Hs256(string issuer, string secret) =>
        new JwtHandler().CreateToken(new JwtConfiguration(
            3600, issuer, JwksTestSupport.Audience, secret, IdentityClaims(localClaim: true)));

    private static string Es256() =>
        new JwtHandler().CreateToken(new JwtConfiguration(
            3600, JwksTestSupport.Issuer, JwksTestSupport.Audience, string.Empty, IdentityClaims(localClaim: true))
        {
            Algorithm = JwtAlgorithms.ES256,
            Keys = [HeimdallKey],
            SigningKeyId = HeimdallKey.Id
        });

    /// <summary>
    /// The request path: validation by Fortuna's keys, the <c>OnTokenValidated</c> step, then the claims
    /// dictionary <see cref="AuthenticatedActorMiddleware" /> builds and the configured mapper. Every token
    /// here carries <c>fortuna_local: true</c>. The result is <see langword="null" /> when the token does not
    /// validate.
    /// </summary>
    private static async Task<bool?> IsLocalOnTheRequestPathAsync(string token, FortunaOptions options)
    {
        var keys = await JwksTestSupport.SigningKeysAsync(options, HeimdallKey);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, JwksTestSupport.ValidationParameters(keys));

        if (!result.IsValid)
        {
            return null;
        }

        Assert.True(new JsonWebToken(token).TryGetPayloadValue<string>(FortunaIdentityMapper.LocalIdentityClaim, out _));
        var context = new TokenValidatedContext(
            new DefaultHttpContext(),
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(result.ClaimsIdentity),
            SecurityToken = result.SecurityToken
        };
        await LocalIdentityRule.OnTokenValidatedAsync(context, options);
        var claims = context.Principal!.Claims
            .GroupBy(claim => claim.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

        return IsLocal(LocalIdentityRule.From(options), claims);
    }

    [UnitFact]
    public void GivenTheLocalIssuer_WhenMapped_ThenTheActorIsLocal()
    {
        Assert.True(IsLocal(new LocalIdentityRule(LocalIssuer, AcceptsLegacyLocalClaim: false), Claims(LocalIssuer, localClaim: false)));
    }

    [UnitFact]
    public void GivenHeimdallsIssuerWithTheLegacyClaim_WhenTheOverlapLasts_ThenTheActorIsLocal()
    {
        Assert.True(IsLocal(new LocalIdentityRule(LocalIssuer, AcceptsLegacyLocalClaim: true), Claims("heimdall-tests", localClaim: true)));
    }

    [UnitFact]
    public void GivenHeimdallsIssuerWithTheLegacyClaim_WhenTheOverlapHasEnded_ThenTheActorIsNotLocal()
    {
        Assert.False(IsLocal(new LocalIdentityRule(LocalIssuer, AcceptsLegacyLocalClaim: false), Claims("heimdall-tests", localClaim: true)));
    }

    [UnitFact]
    public void GivenHeimdallsIssuerWithoutTheClaim_WhenMapped_ThenTheActorIsNotLocal()
    {
        Assert.False(IsLocal(new LocalIdentityRule(LocalIssuer, AcceptsLegacyLocalClaim: true), Claims("heimdall-tests", localClaim: false)));
    }

    [UnitFact]
    public void GivenAnIssuerDifferingFromTheLocalOneOnlyInCase_WhenMapped_ThenTheActorIsNotLocal()
    {
        Assert.False(IsLocal(new LocalIdentityRule(LocalIssuer, AcceptsLegacyLocalClaim: false), Claims("Fortuna-Local", localClaim: false)));
    }

    [UnitFact]
    public void GivenTheDefaultRule_WhenATokenUnderHeimdallsIssuerClaimsLocal_ThenTheActorIsNotLocal()
    {
        Assert.False(LocalIdentityRule.Default.AcceptsLegacyLocalClaim);
        Assert.False(IsLocal(LocalIdentityRule.Default, Claims("heimdall-tests", localClaim: true)));
        Assert.False(new FortunaIdentityMapper().FromClaims(Claims("heimdall-tests", localClaim: true)) is FortunaIdentity { IsLocal: true });
    }

    [UnitTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void GivenTheConfiguration_WhenTheRuleIsBuilt_ThenItHonoursTheLegacyClaimOnlyDuringTheOverlap(bool sharedSecret)
    {
        var rule = LocalIdentityRule.From(JwksTestSupport.Options(sharedSecret));

        Assert.Equal(FortunaOptions.DefaultLocalTokenIssuer, rule.LocalIssuer);
        Assert.Equal(sharedSecret, rule.AcceptsLegacyLocalClaim);
    }

    [UnitFact]
    public async Task GivenAnEs256HeimdallTokenClaimingLocal_WhenTheOverlapLasts_ThenTheActorIsNotLocal()
    {
        Assert.False(await IsLocalOnTheRequestPathAsync(Es256(), JwksTestSupport.Options()));
    }

    [UnitFact]
    public async Task GivenAnHs256LegacyTokenClaimingLocal_WhenTheOverlapLasts_ThenTheActorIsLocal()
    {
        var token = Hs256(JwksTestSupport.Issuer, JwksTestSupport.SharedSecret);

        Assert.True(await IsLocalOnTheRequestPathAsync(token, JwksTestSupport.Options()));
    }

    [UnitFact]
    public async Task GivenAnHs256LegacyTokenClaimingLocal_WhenTheOverlapHasEnded_ThenItIsRefused()
    {
        var token = Hs256(JwksTestSupport.Issuer, JwksTestSupport.SharedSecret);

        Assert.Null(await IsLocalOnTheRequestPathAsync(token, JwksTestSupport.Options(sharedSecret: false)));
    }

    [UnitFact]
    public async Task GivenALocalIssuerToken_WhenTheOverlapHasEnded_ThenTheActorIsLocal()
    {
        var token = Hs256(LocalIssuer, JwksTestSupport.LocalSecret);

        Assert.True(await IsLocalOnTheRequestPathAsync(token, JwksTestSupport.Options(sharedSecret: false)));
    }

    /// <summary>A validated HS256 token under any issuer but Heimdall's keeps no claim: only Heimdall's legacy tokens may.</summary>
    [UnitFact]
    public async Task GivenAValidatedLocalIssuerToken_WhenChecked_ThenItDoesNotKeepTheLegacyClaim()
    {
        var keys = await JwksTestSupport.SigningKeysAsync(JwksTestSupport.Options(), HeimdallKey);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            Hs256(LocalIssuer, JwksTestSupport.LocalSecret), JwksTestSupport.ValidationParameters(keys));

        Assert.True(result.IsValid);
        Assert.False(LocalIdentityRule.KeepsLegacyLocalClaim(result.SecurityToken, JwksTestSupport.Options()));
    }

    /// <summary>
    /// The validation step on its own: an HS256 token under Heimdall's issuer validated during the overlap
    /// loses the claim when the step runs under the configuration without the shared secret.
    /// </summary>
    [UnitFact]
    public async Task GivenAValidatedLegacyToken_WhenTheStepRunsWithoutTheSharedSecret_ThenTheClaimIsRemoved()
    {
        var keys = await JwksTestSupport.SigningKeysAsync(JwksTestSupport.Options(), HeimdallKey);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            Hs256(JwksTestSupport.Issuer, JwksTestSupport.SharedSecret), JwksTestSupport.ValidationParameters(keys));
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);

        Assert.True(result.IsValid);
        Assert.True(LocalIdentityRule.KeepsLegacyLocalClaim(result.SecurityToken, JwksTestSupport.Options()));
        Assert.False(LocalIdentityRule.KeepsLegacyLocalClaim(result.SecurityToken, JwksTestSupport.Options(sharedSecret: false)));
        LocalIdentityRule.RemoveLegacyLocalClaims(principal);
        Assert.DoesNotContain(principal.Claims, claim => claim.Type == FortunaIdentityMapper.LocalIdentityClaim);
    }
}
