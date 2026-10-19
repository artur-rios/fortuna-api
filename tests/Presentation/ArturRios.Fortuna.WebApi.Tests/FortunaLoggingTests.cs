using System.Collections.Concurrent;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Extensions;
using ArturRios.Fortuna.WebApi.Observability;
using ArturRios.Util.Test.Attributes;
using ArturRios.Util.WebApi.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Runs alone: the start-up lines go through Serilog's static logger, which these tests replace
///     and which every host a factory starts replaces too.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StaticLoggerCollection
{
    public const string Name = "Static Serilog logger";
}

[Collection(StaticLoggerCollection.Name)]
public sealed class FortunaLoggingTests
{
    private const string HeimdallTransportTemplate =
        "Heimdall credential exchange: {HeimdallBaseUrl} over {Transport}; caller key {CallerKeyState}";

    private const string CallerKey = "caller-key-with-at-least-32-characters-0123";
    [UnitTheory]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information, false)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning, true)]
    [InlineData("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogEventLevel.Information, false)]
    [InlineData("Microsoft.Hosting.Lifetime", LogEventLevel.Information, true)]
    [InlineData("ArturRios.Fortuna.WebApi.Services.ExchangeRateSyncHostedService", LogEventLevel.Information, true)]
    [InlineData("ArturRios.Fortuna.WebApi.Services.ExchangeRateSyncHostedService", LogEventLevel.Debug, false)]
    public void GivenDefaultConfiguration_WhenLogging_ThenFrameworkNoiseIsSuppressed(
        string sourceContext,
        LogEventLevel level,
        bool enabled)
    {
        using var logger = CreateLogger(new Dictionary<string, string?>());

        Assert.Equal(enabled, logger.ForContext(Constants.SourceContextPropertyName, sourceContext).IsEnabled(level));
    }

    [UnitFact]
    public void GivenDefaultConfiguration_WhenTraceActivityMiddlewareLogsARequest_ThenItIsWritten()
    {
        // The per-request entry carrying the client IP address is written at Information; no
        // override may hold the library's namespace below that.
        using var logger = CreateLogger(new Dictionary<string, string?>());

        Assert.True(logger.ForContext(Constants.SourceContextPropertyName, typeof(TraceActivityMiddleware).FullName)
            .IsEnabled(LogEventLevel.Information));
    }

    [UnitFact]
    public void GivenSerilogConfigurationSection_WhenLogging_ThenConfiguredLevelsWin()
    {
        using var logger = CreateLogger(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Debug",
            ["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Information"
        });

        Assert.True(logger.ForContext(Constants.SourceContextPropertyName, "Microsoft.EntityFrameworkCore.Database.Command")
            .IsEnabled(LogEventLevel.Information));
        Assert.True(logger.ForContext(Constants.SourceContextPropertyName, "ArturRios.Fortuna.WebApi")
            .IsEnabled(LogEventLevel.Debug));
    }

    [UnitFact]
    public void GivenPlainHttpHeimdallUrl_WhenStarting_ThenTheTransportIsLogged()
    {
        var options = Options() with
        {
            HeimdallBaseUri = new Uri("http://heimdall-api.production:8080/"),
            HeimdallCallerKey = CallerKey
        };

        var events = CaptureStartup(options);

        var line = Assert.Single(events, logEvent => logEvent.MessageTemplate.Text == HeimdallTransportTemplate);
        Assert.Equal(LogEventLevel.Information, line.Level);
        Assert.Equal("http://heimdall-api.production:8080/", Scalar(line, "HeimdallBaseUrl"));
        Assert.Equal("plain HTTP to private addresses only", Scalar(line, "Transport"));
        Assert.Equal("configured", Scalar(line, "CallerKeyState"));
        Assert.All(events, logEvent =>
        {
            Assert.DoesNotContain(CallerKey, logEvent.RenderMessage(), StringComparison.Ordinal);
            Assert.All(logEvent.Properties.Values, value =>
                Assert.DoesNotContain(CallerKey, value.ToString(), StringComparison.Ordinal));
        });
    }

    [UnitFact]
    public void GivenHttpsHeimdallUrl_WhenStarting_ThenTlsIsLogged()
    {
        var options = Options() with { HeimdallBaseUri = new Uri("https://heimdall.example.test/") };

        var events = CaptureStartup(options);

        var line = Assert.Single(events, logEvent => logEvent.MessageTemplate.Text == HeimdallTransportTemplate);
        Assert.Equal("https://heimdall.example.test/", Scalar(line, "HeimdallBaseUrl"));
        Assert.Equal("TLS", Scalar(line, "Transport"));
        Assert.Equal("not configured", Scalar(line, "CallerKeyState"));
    }

    private static IReadOnlyList<LogEvent> CaptureStartup(FortunaOptions options)
    {
        var sink = new CollectingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            new ServiceCollection().AddFortunaIntegrations(options);
        }
        finally
        {
            (Log.Logger as IDisposable)?.Dispose();
            Log.Logger = previous;
        }

        return sink.Events.ToArray();
    }

    private static string? Scalar(LogEvent logEvent, string property) =>
        logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private static Logger CreateLogger(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return FortunaLogging.Configure(new LoggerConfiguration(), configuration, Options()).CreateLogger();
    }

    private static FortunaOptions Options() =>
        new()
        {
            DataConnectionString = "unused",
            DataDatabaseType = "PostgreSql",
            StorageProvider = "Filesystem",
            StoragePath = Path.Combine(Path.GetTempPath(), "fortuna-api-logging-tests-storage"),
            LogDirectory = Path.Combine(Path.GetTempPath(), "fortuna-api-logging-tests"),
            AuthTokenSecret = "fortuna-tests-signing-key-with-enough-entropy",
            AuthTokenIssuer = "heimdall-tests",
            AuthTokenAudience = "fortuna-tests",
            Locale = "pt-BR",
            ConsentExternalDataProcessingVersion = "1.0",
            HeimdallBaseUri = new Uri("https://heimdall.example.test/"),
            HeimdallJwksUri = new Uri("http://heimdall.example.test/.well-known/jwks.json"),
            LocalTokenSecret = "fortuna-tests-local-token-secret-with-enough-entropy",
            LocalTokenIssuer = FortunaOptions.DefaultLocalTokenIssuer
        };

    private sealed class CollectingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
