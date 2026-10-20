using System.Security.Claims;
using System.Text;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// What makes a token a local-account token: Fortuna's own issuer alone. The fortuna_local claim never does,
/// and validation removes it from every token under another issuer.
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
        Assert.True(IsLocal(new LocalIdentityRule(LocalIssuer), Claims(LocalIssuer, localClaim: false)));
    }

    [UnitFact]
    public void GivenHeimdallsIssuerWithTheLegacyClaim_WhenMapped_ThenTheActorIsNotLocal()
    {
        Assert.False(IsLocal(new LocalIdentityRule(LocalIssuer), Claims("heimdall-tests", localClaim: true)));
    }

    [UnitFact]
    public void GivenHeimdallsIssuerWithoutTheClaim_WhenMapped_ThenTheActorIsNotLocal()
    {
        Assert.False(IsLocal(new LocalIdentityRule(LocalIssuer), Claims("heimdall-tests", localClaim: false)));
    }

    [UnitFact]
    public void GivenAnIssuerDifferingFromTheLocalOneOnlyInCase_WhenMapped_ThenTheActorIsNotLocal()
    {
        Assert.False(IsLocal(new LocalIdentityRule(LocalIssuer), Claims("Fortuna-Local", localClaim: false)));
    }

    [UnitFact]
    public void GivenTheDefaultRule_WhenATokenUnderHeimdallsIssuerClaimsLocal_ThenTheActorIsNotLocal()
    {
        Assert.False(IsLocal(LocalIdentityRule.Default, Claims("heimdall-tests", localClaim: true)));
        Assert.False(new FortunaIdentityMapper().FromClaims(Claims("heimdall-tests", localClaim: true)) is FortunaIdentity { IsLocal: true });
    }

    [UnitFact]
    public void GivenTheConfiguration_WhenTheRuleIsBuilt_ThenItNamesTheConfiguredLocalIssuer()
    {
        var settings = JwksTestSupport.Settings(retiredSecret: true);
        settings["FORTUNA_LOCAL_TOKEN_ISSUER"] = "fortuna-desktop";

        Assert.Equal(new LocalIdentityRule("fortuna-desktop"), LocalIdentityRule.From(FortunaOptions.From(settings.GetValueOrDefault)));
        Assert.Equal(FortunaOptions.DefaultLocalTokenIssuer, LocalIdentityRule.From(JwksTestSupport.Options()).LocalIssuer);
    }

    [UnitFact]
    public async Task GivenAnEs256HeimdallTokenClaimingLocal_WhenValidated_ThenTheActorIsNotLocal()
    {
        Assert.False(await IsLocalOnTheRequestPathAsync(Es256(), JwksTestSupport.Options(retiredSecret: true)));
    }

    [UnitTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenAnHs256LegacyTokenClaimingLocal_WhenValidated_ThenItIsRefused(bool retiredSecret)
    {
        var token = Hs256(JwksTestSupport.Issuer, JwksTestSupport.SharedSecret);

        Assert.Null(await IsLocalOnTheRequestPathAsync(token, JwksTestSupport.Options(retiredSecret)));
    }

    [UnitFact]
    public async Task GivenALocalIssuerToken_WhenValidated_ThenTheActorIsLocal()
    {
        var token = Hs256(LocalIssuer, JwksTestSupport.LocalSecret);

        Assert.True(await IsLocalOnTheRequestPathAsync(token, JwksTestSupport.Options()));
    }

    /// <summary>
    /// A local token keeps the claim fortuna-ui reads: the step removes it only from tokens under another
    /// issuer.
    /// </summary>
    [UnitFact]
    public async Task GivenAValidatedLocalIssuerToken_WhenTheStepRuns_ThenItKeepsItsClaim()
    {
        var options = JwksTestSupport.Options();
        var keys = await JwksTestSupport.SigningKeysAsync(options, HeimdallKey);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            Hs256(LocalIssuer, JwksTestSupport.LocalSecret), JwksTestSupport.ValidationParameters(keys));
        var context = new TokenValidatedContext(
            new DefaultHttpContext(),
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(result.ClaimsIdentity),
            SecurityToken = result.SecurityToken
        };

        Assert.True(result.IsValid);

        await LocalIdentityRule.OnTokenValidatedAsync(context, options);

        Assert.Contains(context.Principal!.Claims, claim => claim.Type == FortunaIdentityMapper.LocalIdentityClaim);
    }

    /// <summary>
    /// The validation step on its own, as if a validator had accepted Heimdall's HS256 token with the retired
    /// secret still set: the <c>fortuna_local</c> claim it carries is removed, so it could never make the
    /// actor local.
    /// </summary>
    [UnitFact]
    public async Task GivenAnHs256HeimdallTokenClaimingFortunaLocal_WhenValidated_ThenTheClaimIsRemoved()
    {
        var options = JwksTestSupport.Options(retiredSecret: true);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            Hs256(JwksTestSupport.Issuer, JwksTestSupport.SharedSecret),
            new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(JwksTestSupport.SharedSecret)),
                ValidIssuer = JwksTestSupport.Issuer,
                ValidAudience = JwksTestSupport.Audience
            });
        var context = new TokenValidatedContext(
            new DefaultHttpContext(),
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(result.ClaimsIdentity),
            SecurityToken = result.SecurityToken
        };

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Contains(context.Principal.Claims, claim => claim.Type == FortunaIdentityMapper.LocalIdentityClaim);

        await LocalIdentityRule.OnTokenValidatedAsync(context, options);

        Assert.DoesNotContain(context.Principal!.Claims, claim =>
            string.Equals(claim.Type, FortunaIdentityMapper.LocalIdentityClaim, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The identity a validated JSON web token yields compares claim types ordinally, so a claim spelled in
    /// another case is not found by an exact lookup. The step removes every spelling of the claim.
    /// </summary>
    [UnitFact]
    public async Task GivenAnEs256TokenWithTheLocalClaimInSeveralCases_WhenTheStepRuns_ThenNoSpellingRemains()
    {
        var options = JwksTestSupport.Options();
        var keys = await JwksTestSupport.SigningKeysAsync(options, HeimdallKey);
        var claims = IdentityClaims(localClaim: true);
        claims["FORTUNA_LOCAL"] = bool.TrueString;
        claims["Fortuna_Local"] = bool.TrueString;
        var token = new JwtHandler().CreateToken(new JwtConfiguration(
            3600, JwksTestSupport.Issuer, JwksTestSupport.Audience, string.Empty, claims)
        {
            Algorithm = JwtAlgorithms.ES256,
            Keys = [HeimdallKey],
            SigningKeyId = HeimdallKey.Id
        });
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, JwksTestSupport.ValidationParameters(keys));
        var context = new TokenValidatedContext(
            new DefaultHttpContext(),
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(result.ClaimsIdentity),
            SecurityToken = result.SecurityToken
        };

        Assert.True(result.IsValid);
        Assert.Equal(3, context.Principal.Claims.Count(claim =>
            string.Equals(claim.Type, FortunaIdentityMapper.LocalIdentityClaim, StringComparison.OrdinalIgnoreCase)));

        await LocalIdentityRule.OnTokenValidatedAsync(context, options);

        Assert.DoesNotContain(context.Principal!.Claims, claim =>
            string.Equals(claim.Type, FortunaIdentityMapper.LocalIdentityClaim, StringComparison.OrdinalIgnoreCase));
    }
}
