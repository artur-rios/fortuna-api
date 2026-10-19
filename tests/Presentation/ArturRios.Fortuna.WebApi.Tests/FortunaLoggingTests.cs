using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Extensions;
using ArturRios.Fortuna.WebApi.Observability;
using ArturRios.Util.Test.Attributes;
using ArturRios.Util.WebApi.Middleware;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class FortunaLoggingTests
{
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

    /// <summary>
    /// The retired secrets are no longer read, so nothing checks them any more: an operator learns they can
    /// go from one warning each, which names the variable and never carries its value.
    /// </summary>
    [UnitFact]
    public void GivenTheRetiredSecretIsSet_WhenStarting_ThenAWarningNamesItAndNeverItsValue()
    {
        const string previous = "fortuna-tests-retired-previous-signing-key";
        var settings = JwksTestSupport.Settings(retiredSecret: true);
        settings["FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS"] = previous;
        var sink = new CollectingSink();

        using (var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger())
        {
            FortunaWebApiServiceCollectionExtensions.LogBearerTokenSettings(
                logger, FortunaOptions.From(settings.GetValueOrDefault));
        }

        var warnings = sink.Events.Where(logEvent => logEvent.Level == LogEventLevel.Warning).ToList();
        var startup = Assert.Single(sink.Events, logEvent => logEvent.Level == LogEventLevel.Information);

        Assert.Equal(
            new object?[] { "FORTUNA_AUTH_TOKEN_SECRET", "FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS" },
            warnings.Select(warning => Scalar(warning, "Variable")));
        Assert.All(warnings, warning =>
            Assert.Equal("{Variable} is set but no longer read; remove it", warning.MessageTemplate.Text));
        Assert.Equal("refused", Scalar(startup, "LegacyState"));
        Assert.All(sink.Events, logEvent =>
        {
            var written = logEvent.RenderMessage() + string.Join('|', logEvent.Properties.Values);

            Assert.DoesNotContain(JwksTestSupport.SharedSecret, written, StringComparison.Ordinal);
            Assert.DoesNotContain(previous, written, StringComparison.Ordinal);
        });
    }

    private static object? Scalar(LogEvent logEvent, string property) =>
        Assert.IsType<ScalarValue>(logEvent.Properties[property]).Value;

    private static Logger CreateLogger(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new FortunaOptions
        {
            DataConnectionString = "unused",
            DataDatabaseType = "PostgreSql",
            StorageProvider = "Filesystem",
            LogDirectory = Path.Combine(Path.GetTempPath(), "fortuna-api-logging-tests"),
            AuthTokenIssuer = "heimdall-tests",
            AuthTokenAudience = "fortuna-tests",
            Locale = "pt-BR",
            ConsentExternalDataProcessingVersion = "1.0",
            HeimdallBaseUri = new Uri("https://heimdall.example.test/"),
            HeimdallJwksUri = new Uri("http://heimdall.example.test/.well-known/jwks.json"),
            LocalTokenSecret = "fortuna-tests-local-token-secret-with-enough-entropy",
            LocalTokenIssuer = FortunaOptions.DefaultLocalTokenIssuer
        };

        return FortunaLogging.Configure(new LoggerConfiguration(), configuration, options).CreateLogger();
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Add(logEvent);
    }
}
