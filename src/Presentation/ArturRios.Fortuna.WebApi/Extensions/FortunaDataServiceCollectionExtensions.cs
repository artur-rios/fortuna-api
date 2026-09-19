using ArturRios.Fortuna.Data.Accounts;
using ArturRios.Fortuna.Data.Attachments;
using ArturRios.Fortuna.Data.Auditing;
using ArturRios.Fortuna.Data.Cards;
using ArturRios.Fortuna.Data.Classification;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Currencies;
using ArturRios.Fortuna.Data.Exports;
using ArturRios.Fortuna.Data.Ingestion;
using ArturRios.Fortuna.Data.Investments;
using ArturRios.Fortuna.Data.Jobs;
using ArturRios.Fortuna.Data.Planning;
using ArturRios.Fortuna.Data.Projections;
using ArturRios.Fortuna.Data.Reporting;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Data.Transactions;
using ArturRios.Fortuna.Data.Users;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Projections;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.WebApi.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace ArturRios.Fortuna.WebApi.Extensions;

public static class FortunaDataServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the database context, data protection and the EF Core stores and readers.
    /// </summary>
    public static IServiceCollection AddFortunaData(
        this IServiceCollection services,
        FortunaOptions options,
        IHostEnvironment environment)
    {
        services.AddSingleton(new DatabaseDiagnosticsOptions(
            SensitiveDataLogging: false,
            DetailedErrors: !environment.IsProduction()));
        services.AddDbContext<AppDbContext>((_, database) => DatabaseProvider.Configure(
            database,
            options.DataDatabaseType,
            options.DataConnectionString));
        services.AddDataProtection()
            .PersistKeysToDbContext<AppDbContext>()
            .SetApplicationName("Fortuna");
        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<ICurrencyReader, EfCurrencyReader>();
        services.AddScoped<EfExchangeRateStore>();
        services.AddScoped<IExchangeRateStore>(provider =>
            provider.GetRequiredService<EfExchangeRateStore>());
        services.AddScoped<IExchangeRateReader>(provider =>
            provider.GetRequiredService<EfExchangeRateStore>());
        services.AddScoped<EfAuditEntryStore>();
        services.AddScoped<IAuditEntryStore>(provider =>
            provider.GetRequiredService<EfAuditEntryStore>());
        services.AddScoped<IAuditEntryReader>(provider =>
            provider.GetRequiredService<EfAuditEntryStore>());
        services.AddScoped<EfFinancialAccountStore>();
        services.AddScoped<IFinancialAccountStore>(provider =>
            provider.GetRequiredService<EfFinancialAccountStore>());
        services.AddScoped<IFinancialAccountReader>(provider =>
            provider.GetRequiredService<EfFinancialAccountStore>());
        services.AddScoped<IFinancialAccountUpdater>(provider =>
            provider.GetRequiredService<EfFinancialAccountStore>());
        services.AddScoped<IFinancialAccountLifecycleStore>(provider =>
            provider.GetRequiredService<EfFinancialAccountStore>());
        services.AddScoped<EfCreditCardStore>();
        services.AddScoped<ICreditCardStore>(provider =>
            provider.GetRequiredService<EfCreditCardStore>());
        services.AddScoped<ICreditCardReader>(provider =>
            provider.GetRequiredService<EfCreditCardStore>());
        services.AddScoped<ICreditCardUpdater>(provider =>
            provider.GetRequiredService<EfCreditCardStore>());
        services.AddScoped<ICreditCardLifecycleStore>(provider =>
            provider.GetRequiredService<EfCreditCardStore>());
        services.AddScoped<EfCreditCardStatementStore>();
        services.AddScoped<ICreditCardStatementCloser>(provider =>
            provider.GetRequiredService<EfCreditCardStatementStore>());
        services.AddScoped<ICreditCardStatementReader>(provider =>
            provider.GetRequiredService<EfCreditCardStatementStore>());
        services.AddScoped<ICreditCardStatementSettlementStore>(provider =>
            provider.GetRequiredService<EfCreditCardStatementStore>());
        services.AddScoped<EfTransactionStore>();
        services.AddScoped<ITransactionStore>(provider =>
            provider.GetRequiredService<EfTransactionStore>());
        services.AddScoped<ITransactionReader>(provider =>
            provider.GetRequiredService<EfTransactionStore>());
        services.AddScoped<ITransactionUpdater>(provider =>
            provider.GetRequiredService<EfTransactionStore>());
        services.AddScoped<ITransactionLifecycleStore>(provider =>
            provider.GetRequiredService<EfTransactionStore>());
        services.AddScoped<ITransactionReconciliationStore>(provider =>
            provider.GetRequiredService<EfTransactionStore>());
        services.AddScoped<EfAttachmentMetadataStore>();
        services.AddScoped<IAttachmentMetadataStore>(provider =>
            provider.GetRequiredService<EfAttachmentMetadataStore>());
        services.AddScoped<IAttachmentMetadataReader>(provider =>
            provider.GetRequiredService<EfAttachmentMetadataStore>());
        services.AddScoped<IAttachmentLifecycleStore, EfAttachmentLifecycleStore>();
        services.AddScoped<EfCategoryStore>();
        services.AddScoped<ICategoryStore>(provider =>
            provider.GetRequiredService<EfCategoryStore>());
        services.AddScoped<ICategoryReader>(provider =>
            provider.GetRequiredService<EfCategoryStore>());
        services.AddScoped<ICategoryUpdater>(provider =>
            provider.GetRequiredService<EfCategoryStore>());
        services.AddScoped<ICategoryTransactionReassigner>(provider =>
            provider.GetRequiredService<EfCategoryStore>());
        services.AddScoped<ICategoryLifecycleStore>(provider =>
            provider.GetRequiredService<EfCategoryStore>());
        services.AddScoped<EfTagStore>();
        services.AddScoped<ITagStore>(provider => provider.GetRequiredService<EfTagStore>());
        services.AddScoped<ITagReader>(provider => provider.GetRequiredService<EfTagStore>());
        services.AddScoped<ITagUpdater>(provider => provider.GetRequiredService<EfTagStore>());
        services.AddScoped<ITagLifecycleStore>(provider =>
            provider.GetRequiredService<EfTagStore>());
        services.AddScoped<ITransactionTagStore>(provider =>
            provider.GetRequiredService<EfTagStore>());
        services.AddScoped<EfCounterpartyStore>();
        services.AddScoped<ICounterpartyStore>(provider =>
            provider.GetRequiredService<EfCounterpartyStore>());
        services.AddScoped<ICounterpartyReader>(provider =>
            provider.GetRequiredService<EfCounterpartyStore>());
        services.AddScoped<ICounterpartyUpdater>(provider =>
            provider.GetRequiredService<EfCounterpartyStore>());
        services.AddScoped<ICounterpartyLifecycleStore>(provider =>
            provider.GetRequiredService<EfCounterpartyStore>());
        services.AddScoped<ICounterpartyMerger>(provider =>
            provider.GetRequiredService<EfCounterpartyStore>());
        services.AddScoped<ICounterpartyCategorySuggester>(provider =>
            provider.GetRequiredService<EfCounterpartyStore>());
        services.AddScoped<EfBudgetStore>();
        services.AddScoped<IBudgetStore>(provider =>
            provider.GetRequiredService<EfBudgetStore>());
        services.AddScoped<IBudgetReader>(provider =>
            provider.GetRequiredService<EfBudgetStore>());
        services.AddScoped<IBudgetUpdater>(provider =>
            provider.GetRequiredService<EfBudgetStore>());
        services.AddScoped<IBudgetLifecycleStore>(provider =>
            provider.GetRequiredService<EfBudgetStore>());
        services.AddScoped<IBudgetConsumptionReader>(provider =>
            provider.GetRequiredService<EfBudgetStore>());
        services.AddScoped<EfGoalStore>();
        services.AddScoped<IGoalStore>(provider =>
            provider.GetRequiredService<EfGoalStore>());
        services.AddScoped<IGoalReader>(provider =>
            provider.GetRequiredService<EfGoalStore>());
        services.AddScoped<IGoalUpdater>(provider =>
            provider.GetRequiredService<EfGoalStore>());
        services.AddScoped<IGoalLifecycleStore>(provider =>
            provider.GetRequiredService<EfGoalStore>());
        services.AddScoped<IGoalProgressReader>(provider =>
            provider.GetRequiredService<EfGoalStore>());
        services.AddScoped<EfTransferStore>();
        services.AddScoped<ITransferStore>(provider =>
            provider.GetRequiredService<EfTransferStore>());
        services.AddScoped<ITransferReader>(provider =>
            provider.GetRequiredService<EfTransferStore>());
        services.AddScoped<ITransferLifecycleStore>(provider =>
            provider.GetRequiredService<EfTransferStore>());
        services.AddScoped<EfInstallmentPlanStore>();
        services.AddScoped<IInstallmentPlanStore>(provider =>
            provider.GetRequiredService<EfInstallmentPlanStore>());
        services.AddScoped<IInstallmentPlanReader>(provider =>
            provider.GetRequiredService<EfInstallmentPlanStore>());
        services.AddScoped<IInstallmentPlanLifecycleStore>(provider =>
            provider.GetRequiredService<EfInstallmentPlanStore>());
        services.AddScoped<EfRecurringTransactionStore>();
        services.AddScoped<IRecurringTransactionStore>(provider =>
            provider.GetRequiredService<EfRecurringTransactionStore>());
        services.AddScoped<IRecurringTransactionReader>(provider =>
            provider.GetRequiredService<EfRecurringTransactionStore>());
        services.AddScoped<IRecurringTransactionUpdater>(provider =>
            provider.GetRequiredService<EfRecurringTransactionStore>());
        services.AddScoped<IRecurringTransactionLifecycleStore>(provider =>
            provider.GetRequiredService<EfRecurringTransactionStore>());
        services.AddScoped<IRecurringTransactionMaterializer>(provider =>
            provider.GetRequiredService<EfRecurringTransactionStore>());
        services.AddScoped<EfInvestmentStore>();
        services.AddScoped<IInvestmentStore>(provider =>
            provider.GetRequiredService<EfInvestmentStore>());
        services.AddScoped<IInvestmentReader>(provider =>
            provider.GetRequiredService<EfInvestmentStore>());
        services.AddScoped<IInvestmentUpdater>(provider =>
            provider.GetRequiredService<EfInvestmentStore>());
        services.AddScoped<IInvestmentLifecycleStore>(provider =>
            provider.GetRequiredService<EfInvestmentStore>());
        services.AddScoped<IInvestmentMovementStore, EfInvestmentMovementStore>();
        services.AddScoped<IInvestmentValuationStore, EfInvestmentValuationStore>();
        services.AddScoped<EfConnectionStore>();
        services.AddScoped<IConnectionStore>(provider =>
            provider.GetRequiredService<EfConnectionStore>());
        services.AddScoped<IConnectionReader>(provider =>
            provider.GetRequiredService<EfConnectionStore>());
        services.AddScoped<IConnectionReauthenticationStore>(provider =>
            provider.GetRequiredService<EfConnectionStore>());
        services.AddScoped<IConnectionRevocationStore>(provider =>
            provider.GetRequiredService<EfConnectionStore>());
        services.AddScoped<IPluggySynchronizationStore, EfPluggySynchronizationStore>();
        services.AddScoped<IExcelImportStore, EfExcelImportStore>();
        services.AddScoped<IPdfInvoiceImportStore, EfPdfInvoiceImportStore>();
        services.AddScoped<IImportJobReader, EfImportJobReader>();
        services.AddScoped<ITableReportReader, EfTableReportReader>();
        services.AddScoped<ITransactionAggregationReader, EfTransactionAggregationReader>();
        services.AddScoped<INetPositionReader, EfNetPositionReader>();
        services.AddScoped<ICashFlowProjectionReader, EfCashFlowProjectionReader>();
        services.AddScoped<ICommittedObligationReader, EfCommittedObligationReader>();
        services.AddScoped<EfDataExportStore>();
        services.AddScoped<IDataExportStore>(provider =>
            provider.GetRequiredService<EfDataExportStore>());
        services.AddScoped<IDataExportReader>(provider =>
            provider.GetRequiredService<EfDataExportStore>());
        services.AddScoped<IPersonalDataExportStore>(provider =>
            provider.GetRequiredService<EfDataExportStore>());
        services.AddScoped<IPersonalDataArchiveBuilder, EfPersonalDataArchiveBuilder>();
        services.AddScoped<IImportJobRetryStore, EfImportJobRetryStore>();
        services.AddScoped<EfBackgroundJobStore>();
        services.AddScoped<IBackgroundJobStore>(provider =>
            provider.GetRequiredService<EfBackgroundJobStore>());
        services.AddScoped<IBackgroundJobHealthReader>(provider =>
            provider.GetRequiredService<EfBackgroundJobStore>());
        services.AddScoped<EfUserProfileStore>();
        services.AddScoped<IUserProfileReader>(provider =>
            provider.GetRequiredService<EfUserProfileStore>());
        services.AddScoped<IUserProfileProvisioner>(provider =>
            provider.GetRequiredService<EfUserProfileStore>());
        services.AddScoped<IUserErasureStore, EfUserErasureStore>();
        services.AddScoped<EfProcessingConsentStore>();
        services.AddScoped<IProcessingConsentStore>(provider =>
            provider.GetRequiredService<EfProcessingConsentStore>());
        services.AddScoped<IProcessingConsentReader>(provider =>
            provider.GetRequiredService<EfProcessingConsentStore>());
        services.AddScoped<ILocalAccountStore, EfLocalAccountStore>();

        return services;
    }
}
