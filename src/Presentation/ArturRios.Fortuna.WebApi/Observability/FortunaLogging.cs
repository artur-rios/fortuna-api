using System.Globalization;
using ArturRios.Fortuna.WebApi.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace ArturRios.Fortuna.WebApi.Observability;

/// <summary>Serilog set-up: a console logger for startup, then the full host logger.</summary>
public static class FortunaLogging
{
    /// <summary>
    /// Console-only logger used before the configuration is known, so configuration errors are
    /// written in the same JSON shape as every other log line instead of being lost.
    /// </summary>
    public static void UseBootstrapLogger() =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(new JsonFormatter())
            .CreateLogger();

    /// <summary>Replaces the startup logger with the configured host logger.</summary>
    public static void UseHostLogger(IConfiguration configuration, FortunaOptions options)
    {
        Log.CloseAndFlush();
        Log.Logger = Configure(new LoggerConfiguration(), configuration, options).CreateLogger();
    }

    /// <summary>
    /// Framework namespaces are quiet by default (their Information events are per-request noise
    /// that the request-logging middleware already summarizes). Any level can be changed through
    /// the standard <c>Serilog</c> configuration section, for example
    /// <c>Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore=Information</c>.
    /// </summary>
    public static LoggerConfiguration Configure(
        LoggerConfiguration logger,
        IConfiguration configuration,
        FortunaOptions options) => logger
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
        .ReadFrom.Configuration(configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(new JsonFormatter())
        .WriteTo.Map(
            logEvent => logEvent.Timestamp.ToString("yyyy'/'MM", CultureInfo.InvariantCulture),
            (yearMonth, sink) => sink.File(
                new JsonFormatter(),
                Path.Combine(options.LogDirectory, yearMonth, "fortuna-.json"),
                rollingInterval: RollingInterval.Day));
}
