using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// The test suite's insecure secret hashing switch, as start-up sees it: refused with the rest of the configuration
/// anywhere but Development.
/// </summary>
public sealed class SecretHashingOptionTests
{
    [UnitFact]
    public void GivenTheSwitchOnInProduction_WhenConfigurationLoads_ThenStartupIsRejectedNamingTheVariable()
    {
        var values = JwksTestSupport.Settings();
        values[SecretHashing.InsecureTestVariable] = "true";
        values["ASPNETCORE_ENVIRONMENT"] = "Production";

        var rejected = Assert.Throws<FortunaConfigurationException>(() => FortunaOptions.From(values.GetValueOrDefault));

        Assert.Contains(SecretHashing.InsecureTestVariable, rejected.Message, StringComparison.Ordinal);
    }

    [UnitFact]
    public void GivenTheSwitchOnInDevelopment_WhenConfigurationLoads_ThenItLoads()
    {
        var values = JwksTestSupport.Settings();
        values[SecretHashing.InsecureTestVariable] = "true";
        values["ASPNETCORE_ENVIRONMENT"] = "Development";

        var parsed = FortunaOptions.Parse(values.GetValueOrDefault);

        Assert.True(parsed.IsValid, string.Join(Environment.NewLine, parsed.Errors));
    }
}
