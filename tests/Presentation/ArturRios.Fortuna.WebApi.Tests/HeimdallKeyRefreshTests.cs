using System.Text;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// Which bearer tokens make Fortuna look for a Heimdall key it does not know: readable ES256 tokens naming
/// Heimdall's issuer and a kid Fortuna does not know, and nothing else.
/// </summary>
public sealed class HeimdallKeyRefreshTests
{
    /// <summary>A secret like the one Heimdall signed its retired HS256 tokens with.</summary>
    private const string RetiredSecret = "fortuna-tests-retired-heimdall-signing-key";

    private static readonly JwtKey HeimdallKey = JwksTestSupport.NewKey();

    private static string Es256(string issuer, JwtKey? key = null)
    {
        key ??= HeimdallKey;

        return new JwtHandler().CreateToken(new JwtConfiguration(
            3600, issuer, JwksTestSupport.Audience, string.Empty, new Dictionary<string, string> { ["id"] = "42" })
        {
            Algorithm = JwtAlgorithms.ES256,
            Keys = [key],
            SigningKeyId = key.Id
        });
    }

    private static string Hs256(string issuer, string secret) =>
        new JwtHandler().CreateToken(new JwtConfiguration(
            3600, issuer, JwksTestSupport.Audience, secret, new Dictionary<string, string> { ["id"] = "42" }));

    /// <summary>An HS256 token under Heimdall's issuer that names a kid, which no token Heimdall issued does.</summary>
    private static string Hs256WithKid() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwksTestSupport.Issuer,
            Audience = JwksTestSupport.Audience,
            Claims = new Dictionary<string, object> { ["id"] = "42" },
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(RetiredSecret)) { KeyId = "invented" },
                SecurityAlgorithms.HmacSha256)
        });

    private static (HeimdallJwks Jwks, StubJwksHandler Handler, ManualClock Clock) CreateJwks()
    {
        var handler = new StubJwksHandler();
        handler.Serve(JwksTestSupport.KeySet(HeimdallKey));
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var jwks = new HeimdallJwks(
            new StubHttpClientFactory(handler), JwksTestSupport.Options(), clock, NullLogger<HeimdallJwks>.Instance);

        return (jwks, handler, clock);
    }

    private static MessageReceivedContext Request(string token)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer " + token;

        return new MessageReceivedContext(
            httpContext,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions());
    }

    [UnitTheory]
    [InlineData("Bearer ")]
    [InlineData("bearer ")]
    public void GivenAHeimdallTokenNamingAKey_WhenInspected_ThenItsKidIsTheOneToKnow(string scheme)
    {
        Assert.Equal(HeimdallKey.Id, HeimdallKeyRefresh.KeyIdToKnow(scheme + Es256(JwksTestSupport.Issuer), JwksTestSupport.Issuer));
    }

    [UnitFact]
    public void GivenATokenUnderAnotherIssuer_WhenInspected_ThenThereIsNothingToFetch()
    {
        Assert.Null(HeimdallKeyRefresh.KeyIdToKnow("Bearer " + Es256("fortuna-local"), JwksTestSupport.Issuer));
    }

    [UnitFact]
    public void GivenAnHs256HeimdallTokenWithoutAKid_WhenInspected_ThenThereIsNothingToFetch()
    {
        var token = Hs256(JwksTestSupport.Issuer, RetiredSecret);

        Assert.Null(HeimdallKeyRefresh.KeyIdToKnow("Bearer " + token, JwksTestSupport.Issuer));
    }

    [UnitFact]
    public void GivenAnHs256HeimdallTokenNamingAKid_WhenInspected_ThenThereIsNothingToFetch()
    {
        Assert.Null(HeimdallKeyRefresh.KeyIdToKnow("Bearer " + Hs256WithKid(), JwksTestSupport.Issuer));
    }

    [UnitTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer not-a-token")]
    [InlineData("Bearer a.b.c")]
    public void GivenNoReadableBearerToken_WhenInspected_ThenThereIsNothingToFetch(string? authorization)
    {
        Assert.Null(HeimdallKeyRefresh.KeyIdToKnow(authorization, JwksTestSupport.Issuer));
    }

    [UnitFact]
    public async Task GivenAHeimdallTokenNamingAnUnknownKid_WhenItArrives_ThenTheKeySetIsFetched()
    {
        var (jwks, handler, _) = CreateJwks();

        await HeimdallKeyRefresh.BeforeValidationAsync(Request(Es256(JwksTestSupport.Issuer)), JwksTestSupport.Issuer, jwks);

        Assert.Equal(1, handler.Requests);
        Assert.Contains(jwks.Keys, key => key.Id == HeimdallKey.Id);
    }

    [UnitFact]
    public async Task GivenAHeimdallTokenNamingAKnownKid_WhenItArrives_ThenNothingIsFetched()
    {
        var (jwks, handler, clock) = CreateJwks();
        await jwks.EnsureKnownAsync(HeimdallKey.Id, CancellationToken.None);
        clock.Now += HeimdallJwks.MinimumRefreshInterval * 2;

        await HeimdallKeyRefresh.BeforeValidationAsync(Request(Es256(JwksTestSupport.Issuer)), JwksTestSupport.Issuer, jwks);

        Assert.Equal(1, handler.Requests);
    }

    /// <summary>
    /// Heimdall's retired HS256 tokens are refused without a key, and local tokens are verified with the secret
    /// Fortuna holds: looking either up would spend the minute's one fetch, and log a warning, on every such
    /// request.
    /// </summary>
    [UnitTheory]
    [InlineData("legacy")]
    [InlineData("legacy naming a kid")]
    [InlineData("local")]
    [InlineData("local ES256 naming a kid")]
    public async Task GivenALegacyOrLocalToken_WhenItArrives_ThenNothingIsFetched(string kind)
    {
        var (jwks, handler, _) = CreateJwks();
        var token = kind switch
        {
            "legacy" => Hs256(JwksTestSupport.Issuer, RetiredSecret),
            "legacy naming a kid" => Hs256WithKid(),
            "local" => Hs256("fortuna-local", JwksTestSupport.LocalSecret),
            _ => Es256("fortuna-local", JwksTestSupport.NewKey())
        };

        await HeimdallKeyRefresh.BeforeValidationAsync(Request(token), JwksTestSupport.Issuer, jwks);

        Assert.Equal(0, handler.Requests);
    }
}
