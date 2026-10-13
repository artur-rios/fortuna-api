using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class HeimdallCallerKeyOptionTests
{
    private const string Variable = "FORTUNA_HEIMDALL_CALLER_KEY";

    [UnitFact]
    public void GivenNoCallerKey_WhenConfigurationLoads_ThenTheOptionIsNull()
    {
        var options = FortunaOptions.From(Settings(null).GetValueOrDefault);

        Assert.Null(options.HeimdallCallerKey);
    }

    [UnitFact]
    public void GivenABlankCallerKey_WhenConfigurationLoads_ThenTheOptionIsNull()
    {
        var options = FortunaOptions.From(Settings("   ").GetValueOrDefault);

        Assert.Null(options.HeimdallCallerKey);
    }

    [UnitFact]
    public void GivenACallerKeyOf32Characters_WhenConfigurationLoads_ThenItIsKept()
    {
        var key = new string('k', 32);

        var options = FortunaOptions.From(Settings(key).GetValueOrDefault);

        Assert.Equal(key, options.HeimdallCallerKey);
    }

    [UnitFact]
    public void GivenACallerKeyOf31Characters_WhenConfigurationLoads_ThenStartupFailsWithoutTheKey()
    {
        var key = "distinct-" + new string('k', 22);
        Assert.Equal(31, key.Length);

        var exception = Assert.Throws<FortunaConfigurationException>(
            () => FortunaOptions.From(Settings(key).GetValueOrDefault));

        Assert.Contains(Variable, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("distinct", exception.Message, StringComparison.Ordinal);
        Assert.All(exception.Errors, error => Assert.DoesNotContain("distinct", error, StringComparison.Ordinal));
    }

    [UnitTheory]
    [InlineData("distinct-0123456789abcdef\r\nghijklmnopqrstuv")]
    [InlineData("distinct-0123456789abcdef\nghijklmnopqrstuv")]
    [InlineData("distinct-0123456789 abcdefghijklmnopqrstuv")]
    [InlineData("distinct-0123456789-abcdefghij-\u00e7\u00e3o-nopqrstuv")]
    [InlineData("distinct-0123456789abcdefghijklmnopqrstuv\t")]
    public void GivenACallerKeyWithCharactersOutsideVisibleAscii_WhenConfigurationLoads_ThenStartupFailsWithoutTheKey(
        string key)
    {
        Assert.True(key.Length >= 32);

        var exception = Assert.Throws<FortunaConfigurationException>(
            () => FortunaOptions.From(Settings(key).GetValueOrDefault));

        Assert.Contains(Variable, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("distinct", exception.Message, StringComparison.Ordinal);
        Assert.Single(exception.Errors);
    }

    [UnitFact]
    public void GivenACallerKeyOfEveryVisibleAsciiCharacter_WhenConfigurationLoads_ThenItIsKept()
    {
        var key = string.Concat(Enumerable.Range(0x21, 0x7E - 0x21 + 1).Select(code => (char)code));

        var options = FortunaOptions.From(Settings(key).GetValueOrDefault);

        Assert.Equal(key, options.HeimdallCallerKey);
    }

    private static Dictionary<string, string?> Settings(string? callerKey) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_AUTH_TOKEN_SECRET"] = "fortuna-tests-signing-key-with-enough-entropy",
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = "heimdall-tests",
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = "fortuna-tests",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
        ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000076",
        [Variable] = callerKey
    };
}
