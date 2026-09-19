using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Observability;
using ArturRios.Util.Test.Attributes;
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

    private static Logger CreateLogger(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new FortunaOptions
        {
            DataConnectionString = "unused",
            DataDatabaseType = "PostgreSql",
            StorageProvider = "Filesystem",
            LogDirectory = Path.Combine(Path.GetTempPath(), "fortuna-api-logging-tests"),
            AuthTokenSecret = "fortuna-tests-signing-key-with-enough-entropy",
            AuthTokenIssuer = "heimdall-tests",
            AuthTokenAudience = "fortuna-tests",
            Locale = "pt-BR",
            ConsentExternalDataProcessingVersion = "1.0",
            HeimdallBaseUri = new Uri("https://heimdall.example.test/")
        };

        return FortunaLogging.Configure(new LoggerConfiguration(), configuration, options).CreateLogger();
    }
}
