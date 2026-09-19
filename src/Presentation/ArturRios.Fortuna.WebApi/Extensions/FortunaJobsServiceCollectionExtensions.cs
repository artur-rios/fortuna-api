using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Services;

namespace ArturRios.Fortuna.WebApi.Extensions;

public static class FortunaJobsServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the background job queue, its handlers and the hosted services that run them.
    /// </summary>
    public static IServiceCollection AddFortunaJobs(
        this IServiceCollection services,
        FortunaOptions options)
    {
        services.AddSingleton<IBackgroundJobQueue>(new BackgroundJobQueue(options.JobQueueCapacity));
        services.AddScoped<BackgroundJobProcessor>();
        services.AddScoped<IBackgroundJobHandler, RecurringMaterializationJobHandler>();
        services.AddScoped<IBackgroundJobHandler, PluggySynchronizationJobHandler>();
        services.AddScoped<IBackgroundJobHandler, ExcelImportJobHandler>();
        services.AddScoped<IBackgroundJobHandler, PdfInvoiceImportJobHandler>();
        services.AddScoped<IBackgroundJobHandler, DataExportJobHandler>();
        services.AddScoped<IBackgroundJobHandler, PersonalDataExportJobHandler>();
        services.AddHostedService<DatabaseInitializationHostedService>();
        services.AddHostedService<BackgroundJobHostedService>();
        services.AddHostedService<ExchangeRateSyncHostedService>();
        services.AddScoped<IBackgroundJobHandler, ExchangeRateSyncJobHandler>();

        return services;
    }
}
