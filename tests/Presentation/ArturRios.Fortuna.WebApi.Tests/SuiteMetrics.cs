using System.Runtime.CompilerServices;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     The suite runs with Prometheus metrics off (<c>FORTUNA_METRICS_PORT=0</c>); the tests that are about the
///     metrics turn them back on for their own hosts, and <see cref="RestoreEnvironmentAttribute" /> switches them off
///     again after each test.
/// </summary>
/// <remarks>
///     Every functional test builds an API host of its own, and none is collected before the run ends: ASP.NET
///     Core's rate-limiting middleware runs a timer it never stops, which keeps the host it was built in reachable.
///     The OpenTelemetry pipeline is most of what such a host weighs (each instrument preallocates its metric
///     points), so with metrics at their default the test process kept growing for the whole run. heimdall-api
///     found and fixed the same growth the same way (its pull request 296). The variable is set when the test
///     assembly is first used, before any host reads its settings from the environment.
/// </remarks>
internal static class SuiteMetrics
{
    public const string PortVariable = "FORTUNA_METRICS_PORT";

    /// <summary>Port 0 switches the metrics listener and the OpenTelemetry pipeline off.</summary>
    public const string Off = "0";

    [ModuleInitializer]
    internal static void SwitchOff() => Environment.SetEnvironmentVariable(PortVariable, Off);
}
