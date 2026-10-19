using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// The token settings: Heimdall's key set address, Fortuna's own key and issuer for local tokens, and the
/// legacy shared secret that is optional now. Every refusal names the variable and never its value.
/// </summary>
public sealed class TokenKeyOptionTests
{
    private const string LocalSecret = JwksTestSupport.LocalSecret;

    private static FortunaConfigurationException Rejected(Dictionary<string, string?> values) =>
        Assert.Throws<FortunaConfigurationException>(() => FortunaOptions.From(values.GetValueOrDefault));

    [UnitFact]
    public void GivenEveryTokenSetting_WhenConfigurationLoads_ThenEachIsRead()
    {
        var options = FortunaOptions.From(JwksTestSupport.Settings().GetValueOrDefault);

        Assert.Equal(JwksTestSupport.JwksUrl, options.HeimdallJwksUri.AbsoluteUri);
        Assert.Equal(LocalSecret, options.LocalTokenSecret);
        Assert.Equal(FortunaOptions.DefaultLocalTokenIssuer, options.LocalTokenIssuer);
        Assert.Equal(JwksTestSupport.SharedSecret, options.AuthTokenSecret);
        Assert.True(options.AcceptsLegacyTokens);
    }

    [UnitFact]
    public void GivenTheInternalKeySetAddress_WhenConfigurationLoads_ThenItIsKeptAsWrittenOverPlainHttp()
    {
        var values = JwksTestSupport.Settings();
        values["FORTUNA_HEIMDALL_JWKS_URL"] = "http://heimdall-api.production:8080/.well-known/jwks.json";

        var options = FortunaOptions.From(values.GetValueOrDefault);

        Assert.Equal("http://heimdall-api.production:8080/.well-known/jwks.json", options.HeimdallJwksUri.AbsoluteUri);
    }

    [UnitTheory]
    [InlineData("FORTUNA_HEIMDALL_JWKS_URL")]
    [InlineData("FORTUNA_LOCAL_TOKEN_SECRET")]
    public void GivenARequiredTokenSettingMissing_WhenConfigurationLoads_ThenStartupIsRejected(string key)
    {
        var values = JwksTestSupport.Settings();
        values.Remove(key);

        Assert.Contains(key, Rejected(values).Message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("not-a-url")]
    [InlineData("ftp://heimdall.example.test/jwks.json")]
    [InlineData("/.well-known/jwks.json")]
    public void GivenAKeySetAddressThatIsNotHttp_WhenConfigurationLoads_ThenStartupIsRejected(string url)
    {
        var values = JwksTestSupport.Settings();
        values["FORTUNA_HEIMDALL_JWKS_URL"] = url;

        Assert.Contains("FORTUNA_HEIMDALL_JWKS_URL", Rejected(values).Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenAShortLocalSecret_WhenConfigurationLoads_ThenStartupIsRejectedWithoutTheValue()
    {
        const string shortSecret = "too-short-local-secret";
        var values = JwksTestSupport.Settings();
        values["FORTUNA_LOCAL_TOKEN_SECRET"] = shortSecret;

        var message = Rejected(values).Message;

        Assert.Contains("FORTUNA_LOCAL_TOKEN_SECRET", message, StringComparison.Ordinal);
        Assert.DoesNotContain(shortSecret, message, StringComparison.Ordinal);
    }

    [UnitTheory]
    [InlineData("FORTUNA_AUTH_TOKEN_SECRET")]
    [InlineData("FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS")]
    public void GivenALocalSecretEqualToTheSharedSecret_WhenConfigurationLoads_ThenStartupIsRejected(string sharedKey)
    {
        // The shared secret is in other services' hands; a local key equal to it would let them mint
        // Fortuna's local tokens.
        var values = JwksTestSupport.Settings();
        values[sharedKey] = LocalSecret;

        var message = Rejected(values).Message;

        Assert.Contains(
            $"'FORTUNA_LOCAL_TOKEN_SECRET' must differ from '{sharedKey}'",
            message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(LocalSecret, message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenALocalIssuerEqualToHeimdalls_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        // The issuer is what tells a local token from a Heimdall one; equal, they could not be told apart.
        var values = JwksTestSupport.Settings();
        values["FORTUNA_LOCAL_TOKEN_ISSUER"] = JwksTestSupport.Issuer;

        Assert.Contains("FORTUNA_LOCAL_TOKEN_ISSUER", Rejected(values).Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenACustomLocalIssuer_WhenConfigurationLoads_ThenItIsUsedTrimmed()
    {
        var values = JwksTestSupport.Settings();
        values["FORTUNA_LOCAL_TOKEN_ISSUER"] = "  fortuna-desktop  ";

        Assert.Equal("fortuna-desktop", FortunaOptions.From(values.GetValueOrDefault).LocalTokenIssuer);
    }

    [UnitFact]
    public void GivenNoSharedSecret_WhenConfigurationLoads_ThenTheOverlapIsOverAndStartupProceeds()
    {
        var options = FortunaOptions.From(JwksTestSupport.Settings(sharedSecret: false).GetValueOrDefault);

        Assert.Null(options.AuthTokenSecret);
        Assert.False(options.AcceptsLegacyTokens);
    }

    [UnitFact]
    public void GivenAPreviousSharedSecretWithoutTheSecret_WhenConfigurationLoads_ThenStartupIsRejected()
    {
        const string previous = "fortuna-tests-previous-signing-key-with-entropy";
        var values = JwksTestSupport.Settings(sharedSecret: false);
        values["FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS"] = previous;

        var message = Rejected(values).Message;

        Assert.Contains("FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS", message, StringComparison.Ordinal);
        Assert.DoesNotContain(previous, message, StringComparison.Ordinal);
    }
}
