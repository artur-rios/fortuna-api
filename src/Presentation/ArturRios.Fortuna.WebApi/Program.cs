using Amazon.Runtime;
using Amazon.S3;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Jobs;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Integration.Storage;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

var options = FortunaOptions.From(Environment.GetEnvironmentVariable);
ConfigureLogging(options);

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton(new DatabaseDiagnosticsOptions(
        SensitiveDataLogging: !builder.Environment.IsProduction(),
        DetailedErrors: !builder.Environment.IsProduction()));
    builder.Services.AddDbContext<AppDbContext>((services, database) => database.UseNpgsql(
        options.DataConnectionString,
        postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema)));
    builder.Services.AddScoped<DatabaseSeeder>();
    builder.Services.AddScoped<IBackgroundJobStore, EfBackgroundJobStore>();
    builder.Services.AddSingleton<IBackgroundJobQueue>(new BackgroundJobQueue(options.JobQueueCapacity));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<BackgroundJobProcessor>();
    builder.Services.AddHostedService<DatabaseInitializationHostedService>();
    builder.Services.AddHostedService<BackgroundJobHostedService>();

    builder.Services.AddSingleton<IIngestionSource, FileUploadIngestionSource>();
    builder.Services.AddSingleton<IngestionSourceRegistry>();
    RegisterAttachmentStore(builder.Services, options);

    builder.Services.AddControllers();
    builder.Services.AddAuthorization();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(document => document.SwaggerDoc("v1", new()
    {
        Title = "Fortuna API",
        Version = "v1"
    }));

    var app = builder.Build();
    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging(logging =>
        logging.GetLevel = (_, _, _) => LogEventLevel.Information);
    app.UseAuthorization();
    app.MapControllers();
    app.Run();
}
finally
{
    await Log.CloseAndFlushAsync();
}

static void ConfigureLogging(FortunaOptions options)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(new JsonFormatter())
        .WriteTo.Map(
            logEvent => logEvent.Timestamp.ToString("yyyy/MM"),
            (yearMonth, sink) => sink.File(
                new JsonFormatter(),
                Path.Combine(options.LogDirectory, yearMonth, "fortuna-.json"),
                rollingInterval: RollingInterval.Day))
        .CreateLogger();
}

static void RegisterAttachmentStore(IServiceCollection services, FortunaOptions options)
{
    if (string.Equals(options.StorageProvider, "Filesystem", StringComparison.OrdinalIgnoreCase))
    {
        services.AddSingleton<IAttachmentStore>(new FilesystemAttachmentStore(options.StoragePath!));
        return;
    }

    var s3Config = new AmazonS3Config
    {
        ServiceURL = options.StorageS3Endpoint,
        ForcePathStyle = true
    };
    services.AddSingleton<IAmazonS3>(new AmazonS3Client(
        new BasicAWSCredentials(options.StorageS3AccessKey, options.StorageS3SecretKey),
        s3Config));
    services.AddSingleton<IAttachmentStore>(provider => new S3AttachmentStore(
        provider.GetRequiredService<IAmazonS3>(),
        options.StorageS3Bucket!));
}

public partial class Program;
