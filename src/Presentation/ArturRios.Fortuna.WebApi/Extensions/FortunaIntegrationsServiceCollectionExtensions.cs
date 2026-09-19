using Amazon.Runtime;
using Amazon.S3;
using ArturRios.Fortuna.Data.Exports;
using ArturRios.Fortuna.Data.Health;
using ArturRios.Fortuna.Integration.Exports;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Integration.Rates;
using ArturRios.Fortuna.Integration.Security;
using ArturRios.Fortuna.Integration.Storage;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Services;

namespace ArturRios.Fortuna.WebApi.Extensions;

public static class FortunaIntegrationsServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the external services: Heimdall, the exchange-rate source, Pluggy, the
    ///     ingestion sources, parsers and renderers, attachment storage and the operational health
    ///     checks.
    /// </summary>
    public static IServiceCollection AddFortunaIntegrations(
        this IServiceCollection services,
        FortunaOptions options)
    {
        services.AddSingleton<IExcelWorkbookParser, ExcelWorkbookParser>();
        services.AddSingleton<IPdfInvoiceParser, NubankPdfInvoiceParser>();
        services.AddSingleton<IDataExportRenderer, DataExportRenderer>();
        services.AddHttpClient<IHeimdallAuthGateway, HeimdallAuthGateway>(client =>
        {
            client.BaseAddress = options.HeimdallBaseUri;
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<IRateLimitDelay, RateLimitDelay>();
        services.AddHttpClient<IPtaxRateClient, PtaxRateClient>(client =>
        {
            client.BaseAddress = options.RatesSourceBaseUri ?? new Uri("http://localhost/");
            client.Timeout = HttpRetryPolicy.RequestTimeout;
        });
        services.AddSingleton<IIngestionSource, PluggyIngestionSource>();
        services.AddHttpClient<IPluggyConnectionGateway, PluggyConnectionGateway>(client =>
        {
            client.BaseAddress = options.PluggyBaseUri ?? new Uri("http://localhost/");
            client.Timeout = HttpRetryPolicy.RequestTimeout;
        });
        services.AddHttpClient<IPluggySynchronizationGateway, PluggySynchronizationGateway>(client =>
        {
            client.BaseAddress = options.PluggyBaseUri ?? new Uri("http://localhost/");
            client.Timeout = HttpRetryPolicy.RequestTimeout;
        });
        services.AddHttpClient(OperationalHealthClientNames.Aggregator, client =>
        {
            client.BaseAddress = options.PluggyBaseUri ?? new Uri("http://localhost/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddHttpClient(OperationalHealthClientNames.ExchangeRateSource, client =>
        {
            client.BaseAddress = options.RatesSourceBaseUri ?? new Uri("http://localhost/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddScoped<IOperationalHealthCheck, DatabaseOperationalHealthCheck>();
        services.AddScoped<IOperationalHealthCheck,
            AttachmentStorageOperationalHealthCheck>();
        services.AddScoped<IOperationalHealthCheck, JobRunnerOperationalHealthCheck>();
        services.AddScoped<IOperationalHealthCheck>(provider =>
            new ExternalServiceOperationalHealthCheck(
                "Aggregator",
                !options.LocalAuthEnabled &&
                    !string.IsNullOrWhiteSpace(options.PluggyClientId) &&
                    !string.IsNullOrWhiteSpace(options.PluggyClientSecret) &&
                    options.PluggyBaseUri is not null,
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(
                    OperationalHealthClientNames.Aggregator)));
        services.AddScoped<IOperationalHealthCheck>(provider =>
            new ExternalServiceOperationalHealthCheck(
                "ExchangeRateSource",
                !options.LocalAuthEnabled && options.RatesSourceBaseUri is not null,
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(
                    OperationalHealthClientNames.ExchangeRateSource)));
        services.AddScoped<OperationalHealthEvaluator>();
        services.AddSingleton<IIngestionSource, ExcelWorkbookIngestionSource>();
        services.AddSingleton<IIngestionSource, NubankInvoiceIngestionSource>();
        services.AddSingleton<IngestionSourceRegistry>();
        services.AddSingleton<IDataSourceCatalog>(provider =>
            provider.GetRequiredService<IngestionSourceRegistry>());
        AddAttachmentStore(services, options);

        return services;
    }

    private static void AddAttachmentStore(IServiceCollection services, FortunaOptions options)
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
}
