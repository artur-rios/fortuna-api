using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Extensions;
using ArturRios.Fortuna.WebApi.Observability;
using Serilog;

FortunaLogging.UseBootstrapLogger();
var parsedOptions = FortunaOptions.Parse(Environment.GetEnvironmentVariable);
if (!parsedOptions.IsValid)
{
    foreach (var error in parsedOptions.Errors)
    {
        Log.Fatal("Invalid configuration: {ConfigurationError}", error);
    }

    await Log.CloseAndFlushAsync();

    throw new FortunaConfigurationException(parsedOptions.Errors);
}

var options = parsedOptions.Options;

try
{
    var builder = WebApplication.CreateBuilder(args);
    FortunaLogging.UseHostLogger(builder.Configuration, options);
    builder.Host.UseSerilog();
    builder.Services
        .AddFortunaOptions(options)
        .AddFortunaData(options, builder.Environment)
        .AddFortunaJobs(options)
        .AddFortunaCommands()
        .AddFortunaQueries()
        .AddFortunaIntegrations(options)
        .AddFortunaWebApi(options);

    var app = builder.Build();
    app.UseFortunaPipeline(options);
    app.Run();
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
