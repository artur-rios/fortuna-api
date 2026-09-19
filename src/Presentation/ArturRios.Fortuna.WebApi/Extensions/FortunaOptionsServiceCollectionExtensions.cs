using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Projections;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.WebApi.Configuration;

namespace ArturRios.Fortuna.WebApi.Extensions;

public static class FortunaOptionsServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the parsed configuration and the per-feature option records derived from it.
    /// </summary>
    public static IServiceCollection AddFortunaOptions(
        this IServiceCollection services,
        FortunaOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton(new PaginationOptions(options.PageSizeMaximum));
        services.AddSingleton(new TransactionAggregationOptions(
            options.ReportMaximumRangeDays));
        services.AddSingleton(new TransactionDrillDownOptions(
            TimeSpan.FromMinutes(options.ReportKeyLifetimeMinutes)));
        services.AddSingleton(new CashFlowProjectionOptions(
            options.ProjectionMaximumHorizonDays,
            90,
            30));
        services.AddSingleton(new DataExportOptions(
            options.ExportSynchronousThresholdRows,
            TimeSpan.FromHours(options.ExportRetentionHours),
            options.Locale));
        services.AddSingleton(new OperationalHealthOptions(
            TimeSpan.FromSeconds(options.HealthJobMaximumPendingSeconds)));
        services.AddSingleton(new ReconciliationOptions(
            options.ReconciliationAmountTolerance,
            options.ReconciliationDateToleranceDays));
        services.AddSingleton(new TagOptions(options.TransactionMaximumTags));
        services.AddSingleton(new ExcelImportOptions(options.ExcelImportMaximumFileBytes));
        services.AddSingleton(new PdfInvoiceImportOptions(
            options.PdfInvoiceImportMaximumFileBytes));
        services.AddSingleton(new AttachmentOptions(
            options.UploadMaximumBytes,
            options.UploadAllowedContentTypes));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new UserProfileProvisioningOptions(
            options.DefaultDisplayCurrency,
            options.Locale));
        services.AddSingleton(new ProcessingConsentOptions(
            options.ConsentExternalDataProcessingVersion));
        services.AddSingleton(new LocalAccountOptions(
            options.LocalAuthEnabled,
            options.LocalAuthRecoveryCodeCount,
            options.DefaultDisplayCurrency,
            options.Locale));
        services.AddSingleton(new HeimdallAuthOptions(options.HeimdallScopeId));
        services.AddSingleton(new RateSyncOptions(
            options.RatesSourceBaseUri,
            options.RatesSyncCron,
            options.RatesCurrencies));
        services.AddSingleton(new PluggySourceOptions(
            options.PluggyClientId,
            options.PluggyClientSecret,
            options.PluggyBaseUri,
            !options.LocalAuthEnabled));

        return services;
    }
}
