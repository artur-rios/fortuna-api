using System.Text.RegularExpressions;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Keeps every test source off a PostgreSQL on the machine running the tests, which may be production.
///     A placeholder connection string must be <see cref="TestDatabase.Unreachable" />.
/// </summary>
public sealed partial class TestDatabaseGuardTests
{
    private const int DefaultPort = 5432;

    private static readonly string[] SourceExtensions =
        [".cs", ".csproj", ".json", ".props", ".targets", ".runsettings", ".config", ".xml"];

    private static readonly string[] BuildOutputDirectories = ["bin", "obj", "TestResults"];

    [UnitFact]
    public void GivenTestSources_WhenScanned_ThenNoConnectionStringReachesALoopbackDatabase()
    {
        var tests = TestsDirectory();

        var offenders = Directory
            .EnumerateFiles(tests, "*", SearchOption.AllDirectories)
            .Where(path => SourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(tests, path))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(BuildOutputDirectories.Contains))
            .SelectMany(path => File
                .ReadLines(Path.Combine(tests, path))
                .Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            .Where(entry => ReachesLoopbackDatabase(entry.Line))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ThenBy(entry => entry.Number)
            .Select(entry => $"tests/{entry.Path.Replace('\\', '/')}:{entry.Number}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} test source line(s) name a PostgreSQL on a loopback host; " +
            $"use TestDatabase.Unreachable instead:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    [UnitTheory]
    [InlineData("Host", "localhost", "", true)]
    [InlineData("Host", "localhost", ";Port=5432", true)]
    [InlineData("Host", "127.0.0.1", "", true)]
    [InlineData("Host", "127.0.0.1", ";Port=5432", true)]
    [InlineData("Host", "127.0.0.1", ":5432", true)]
    [InlineData("Host", "::1", ";Port=6543", true)]
    [InlineData("Server", "localhost", "", true)]
    [InlineData("host", "LOCALHOST", "", true)]
    [InlineData("Host", "127.0.0.1", ";Port=1", false)]
    [InlineData("Host", "localhost", ";Port=1", false)]
    [InlineData("Host", "127.0.0.1", ":1", false)]
    [InlineData("Host", "postgres.example.test", ";Port=5432", false)]
    public void GivenConnectionString_WhenChecked_ThenOnlyALoopbackHostOffPortOneIsFlagged(
        string keyword,
        string host,
        string port,
        bool flagged)
    {
        var line = $"[\"FORTUNA_DATA_CONNECTIONSTRING\"] = \"{keyword}={host}{port};Database=fortuna\",";

        Assert.Equal(flagged, ReachesLoopbackDatabase(line));
    }

    [UnitFact]
    public void GivenUriOnTheLoopbackDefaultPort_WhenChecked_ThenItIsFlagged()
    {
        const string loopback = "localhost";

        Assert.True(ReachesLoopbackDatabase($"\"postgresql://postgres:postgres@{loopback}:{DefaultPort}/fortuna\""));
    }

    [UnitFact]
    public void GivenCodeThatOnlySetsAnHttpHost_WhenChecked_ThenItIsNotFlagged()
    {
        Assert.False(ReachesLoopbackDatabase("request.Headers.Host = $\"localhost:{MetricsPort}\";"));
        Assert.False(ReachesLoopbackDatabase("context.Request.Host = new HostString(\"localhost\", MetricsPort);"));
    }

    /// <summary>
    ///     Whether a line holds a connection string whose host is a loopback address on any port but 1, or
    ///     names the default PostgreSQL port on a loopback host in any form.
    /// </summary>
    private static bool ReachesLoopbackDatabase(string line)
    {
        foreach (Match match in HostSetting().Matches(line))
        {
            var connection = line[match.Index..].Split('"', '\'', '`')[0];
            var port = connection
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(setting => setting.Split('=', 2, StringSplitOptions.TrimEntries))
                .LastOrDefault(setting =>
                    setting.Length == 2 && setting[0].Equals("Port", StringComparison.OrdinalIgnoreCase))?[1];

            foreach (var host in match.Groups["hosts"].Value.Split(',', StringSplitOptions.TrimEntries))
            {
                var (name, hostPort) = SplitPort(host);

                if (IsLoopback(name) && (hostPort ?? port) != "1")
                {
                    return true;
                }
            }
        }

        return LoopbackDefaultPort().IsMatch(line);
    }

    private static (string Name, string? Port) SplitPort(string host)
    {
        if (host.StartsWith('['))
        {
            var end = host.IndexOf(']');

            return end < 0
                ? (host.Trim('['), null)
                : (host[1..end], host[(end + 1)..].StartsWith(':') ? host[(end + 2)..] : null);
        }

        var colons = host.Count(character => character == ':');

        return colons == 1
            ? (host[..host.IndexOf(':')], host[(host.IndexOf(':') + 1)..])
            : (host, null);
    }

    private static bool IsLoopback(string name) =>
        name.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("127.", StringComparison.Ordinal) ||
        name == "::1";

    private static string TestsDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "ArturRios.Fortuna.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests")))
            {
                return Path.Combine(directory.FullName, "tests");
            }
        }

        throw new InvalidOperationException(
            $"No directory above {AppContext.BaseDirectory} holds src/ArturRios.Fortuna.sln and tests/.");
    }

    [GeneratedRegex(@"(?<![\w.])(?:Host|Server)\s*=\s*(?<hosts>[^;""'`\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex HostSetting();

    [GeneratedRegex(@"(?:localhost|127(?:\.\d{1,3}){3}|\[::1\])\s*:\s*5432\b", RegexOptions.IgnoreCase)]
    private static partial Regex LoopbackDefaultPort();
}
