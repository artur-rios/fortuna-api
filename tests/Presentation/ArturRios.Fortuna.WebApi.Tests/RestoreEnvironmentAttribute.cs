using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit.v3;

[assembly: ArturRios.Fortuna.WebApi.Tests.RestoreEnvironment]

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Puts the process's <c>FORTUNA_*</c> and <c>ASPNETCORE_*</c> variables back to what the run started with after
///     every test, so no test sees a setting an earlier one left behind.
/// </summary>
/// <remarks>
///     The API reads its configuration from the process environment, so each test class writes the settings its hosts
///     need there and never takes them out. A class that sets a variable the next one does not mention used to hand it
///     on, and which class ran next depended on the order xUnit picked, which changes whenever classes are added or the
///     run is split into several processes. The baseline is the environment the test process started with (the run
///     settings, <c>default.runsettings</c>), plus what the module initializers set before the first test: the key set's
///     address (<see cref="TestHeimdallKeySet" />) and metrics off (<see cref="SuiteMetrics" />). Those two are set
///     again after every restore, so the order the initializers run in does not matter.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class RestoreEnvironmentAttribute : BeforeAfterTestAttribute
{
    private static readonly string[] Prefixes = ["FORTUNA_", "ASPNETCORE_"];

    private static IReadOnlyDictionary<string, string> baseline = new Dictionary<string, string>();

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        Restore(baseline);
        Environment.SetEnvironmentVariable("FORTUNA_HEIMDALL_JWKS_URL", TestHeimdallKeySet.Url);
        SuiteMetrics.SwitchOff();
    }

    [ModuleInitializer]
    internal static void CaptureBaseline() => baseline = Snapshot();

    /// <summary>Removes every variable <paramref name="baseline" /> does not have and resets the ones it does.</summary>
    internal static void Restore(IReadOnlyDictionary<string, string> baseline)
    {
        foreach (var name in Snapshot().Keys.Where(name => !baseline.ContainsKey(name)))
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var (name, value) in baseline)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    internal static IReadOnlyDictionary<string, string> Snapshot() => Environment
        .GetEnvironmentVariables()
        .Cast<DictionaryEntry>()
        .Select(entry => (Name: (string)entry.Key, Value: entry.Value as string ?? string.Empty))
        .Where(entry => Prefixes.Any(prefix => entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        .ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
}
