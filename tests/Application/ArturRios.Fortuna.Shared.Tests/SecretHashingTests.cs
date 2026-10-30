using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Hashing;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Shared.Tests;

public sealed class SecretHashingTests
{
    private static Func<string, string?> Reading(params (string Name, string? Value)[] values) =>
        name => values.FirstOrDefault(value => value.Name == name).Value;

    [UnitFact]
    public void GivenTheVariableUnset_WhenTheCostIsRead_ThenItIsTheProductionCost()
    {
        var configuration = SecretHashing.FromEnvironment(Reading());

        Assert.Equal(HashConfiguration.DefaultDegreeOfParallelism, configuration.DegreeOfParallelism);
        Assert.Equal(HashConfiguration.DefaultNumberOfIterations, configuration.NumberOfIterations);
        Assert.Equal(HashConfiguration.DefaultMemoryToUseInKb, configuration.MemoryToUseInKb);
    }

    [UnitTheory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" True ")]
    public void GivenTheVariableTrue_WhenTheCostIsRead_ThenItIsTheInsecureCost(string value)
    {
        var configuration = SecretHashing.FromEnvironment(Reading((SecretHashing.InsecureTestVariable, value)));

        Assert.Same(SecretHashing.Insecure, configuration);
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("1")]
    [InlineData("yes")]
    public void GivenTheVariableAnythingButTrue_WhenTheCostIsRead_ThenItIsTheProductionCost(string value)
    {
        var configuration = SecretHashing.FromEnvironment(Reading((SecretHashing.InsecureTestVariable, value)));

        Assert.NotSame(SecretHashing.Insecure, configuration);
        Assert.Equal(HashConfiguration.DefaultMemoryToUseInKb, configuration.MemoryToUseInKb);
    }

    [UnitTheory]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Production")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Staging")]
    [InlineData("DOTNET_ENVIRONMENT", "Production")]
    public void GivenTheSwitchOnOutsideDevelopment_WhenStartingUp_ThenTheErrorNamesTheVariableAndEnvironment(
        string environmentVariable, string environment)
    {
        var error = SecretHashing.StartupError(Reading(
            (SecretHashing.InsecureTestVariable, "true"), (environmentVariable, environment)));

        Assert.NotNull(error);
        Assert.Contains(SecretHashing.InsecureTestVariable, error);
        Assert.Contains(environment, error);
    }

    [UnitFact]
    public void GivenTheSwitchOnAndNoEnvironmentDeclared_WhenStartingUp_ThenItIsRefusedAsProduction()
    {
        var error = SecretHashing.StartupError(Reading((SecretHashing.InsecureTestVariable, "true")));

        Assert.NotNull(error);
        Assert.Contains("Production", error);
    }

    [UnitTheory]
    [InlineData("ASPNETCORE_ENVIRONMENT")]
    [InlineData("DOTNET_ENVIRONMENT")]
    public void GivenTheSwitchOnInDevelopment_WhenStartingUp_ThenThereIsNoError(string environmentVariable)
    {
        var error = SecretHashing.StartupError(Reading(
            (SecretHashing.InsecureTestVariable, "true"), (environmentVariable, "development")));

        Assert.Null(error);
    }

    [UnitFact]
    public void GivenAspNetCoreDevelopmentAndDotNetProduction_WhenStartingUp_ThenAspNetCoreWins()
    {
        var error = SecretHashing.StartupError(Reading(
            (SecretHashing.InsecureTestVariable, "true"),
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("DOTNET_ENVIRONMENT", "Production")));

        Assert.Null(error);
    }

    [UnitFact]
    public void GivenTheSwitchOff_WhenStartingUpInProduction_ThenThereIsNoError()
    {
        var error = SecretHashing.StartupError(Reading(("ASPNETCORE_ENVIRONMENT", "Production")));

        Assert.Null(error);
    }

    [UnitFact]
    public void GivenTheTestRun_WhenTheCostInEffectIsRead_ThenItIsTheInsecureCost()
    {
        // tests/default.runsettings switches it on for every test project.
        Assert.True(SecretHashing.IsInsecure);
        Assert.Same(SecretHashing.Insecure, SecretHashing.Configuration);
    }

    [UnitFact]
    public void GivenTheInsecureCost_WhenDerivingAndVerifying_ThenTheyAgree()
    {
        var hash = Hash.EncodeWithRandomSalt("a secret", out var salt, SecretHashing.Insecure);

        Assert.True(Hash.TextMatches("a secret", hash, salt, SecretHashing.Insecure));
        Assert.False(Hash.TextMatches("another secret", hash, salt, SecretHashing.Insecure));
    }
}
