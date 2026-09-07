using Amazon.Runtime;
using Amazon.S3;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Attachments;
using ArturRios.Fortuna.Data.Accounts;
using ArturRios.Fortuna.Data.Auditing;
using ArturRios.Fortuna.Data.Cards;
using ArturRios.Fortuna.Data.Classification;
using ArturRios.Fortuna.Data.Currencies;
using ArturRios.Fortuna.Data.Jobs;
using ArturRios.Fortuna.Data.Investments;
using ArturRios.Fortuna.Data.Ingestion;
using ArturRios.Fortuna.Data.Planning;
using ArturRios.Fortuna.Data.Reporting;
using ArturRios.Fortuna.Data.Users;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Data.Transactions;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Auditing;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Integration.Rates;
using ArturRios.Fortuna.Integration.Storage;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Fortuna.WebApi.Services;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Jwt;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Mediator.Query;
using ArturRios.Mediator.Query.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using System.Text;

var options = FortunaOptions.From(Environment.GetEnvironmentVariable);
ConfigureLogging(options);

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton(new DatabaseDiagnosticsOptions(
        SensitiveDataLogging: false,
        DetailedErrors: !builder.Environment.IsProduction()));
    builder.Services.AddDbContext<AppDbContext>((services, database) => database.UseNpgsql(
        options.DataConnectionString,
        postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema)));
    builder.Services.AddDataProtection()
        .PersistKeysToDbContext<AppDbContext>()
        .SetApplicationName("Fortuna");
    builder.Services.AddScoped<DatabaseSeeder>();
    builder.Services.AddScoped<ICurrencyReader, EfCurrencyReader>();
    builder.Services.AddScoped<EfExchangeRateStore>();
    builder.Services.AddScoped<IExchangeRateStore>(provider =>
        provider.GetRequiredService<EfExchangeRateStore>());
    builder.Services.AddScoped<IExchangeRateReader>(provider =>
        provider.GetRequiredService<EfExchangeRateStore>());
    builder.Services.AddScoped<EfAuditEntryStore>();
    builder.Services.AddScoped<IAuditEntryStore>(provider =>
        provider.GetRequiredService<EfAuditEntryStore>());
    builder.Services.AddScoped<IAuditEntryReader>(provider =>
        provider.GetRequiredService<EfAuditEntryStore>());
    builder.Services.AddScoped<IAuditEntryWriter, AuditEntryWriter>();
    builder.Services.AddScoped<EfFinancialAccountStore>();
    builder.Services.AddScoped<IFinancialAccountStore>(provider =>
        provider.GetRequiredService<EfFinancialAccountStore>());
    builder.Services.AddScoped<IFinancialAccountReader>(provider =>
        provider.GetRequiredService<EfFinancialAccountStore>());
    builder.Services.AddScoped<IFinancialAccountUpdater>(provider =>
        provider.GetRequiredService<EfFinancialAccountStore>());
    builder.Services.AddScoped<IFinancialAccountLifecycleStore>(provider =>
        provider.GetRequiredService<EfFinancialAccountStore>());
    builder.Services.AddScoped<EfCreditCardStore>();
    builder.Services.AddScoped<ICreditCardStore>(provider =>
        provider.GetRequiredService<EfCreditCardStore>());
    builder.Services.AddScoped<ICreditCardReader>(provider =>
        provider.GetRequiredService<EfCreditCardStore>());
    builder.Services.AddScoped<ICreditCardUpdater>(provider =>
        provider.GetRequiredService<EfCreditCardStore>());
    builder.Services.AddScoped<ICreditCardLifecycleStore>(provider =>
        provider.GetRequiredService<EfCreditCardStore>());
    builder.Services.AddScoped<EfCreditCardStatementStore>();
    builder.Services.AddScoped<ICreditCardStatementCloser>(provider =>
        provider.GetRequiredService<EfCreditCardStatementStore>());
    builder.Services.AddScoped<ICreditCardStatementReader>(provider =>
        provider.GetRequiredService<EfCreditCardStatementStore>());
    builder.Services.AddScoped<ICreditCardStatementSettlementStore>(provider =>
        provider.GetRequiredService<EfCreditCardStatementStore>());
    builder.Services.AddScoped<EfTransactionStore>();
    builder.Services.AddScoped<ITransactionStore>(provider =>
        provider.GetRequiredService<EfTransactionStore>());
    builder.Services.AddScoped<ITransactionReader>(provider =>
        provider.GetRequiredService<EfTransactionStore>());
    builder.Services.AddScoped<ITransactionUpdater>(provider =>
        provider.GetRequiredService<EfTransactionStore>());
    builder.Services.AddScoped<ITransactionLifecycleStore>(provider =>
        provider.GetRequiredService<EfTransactionStore>());
    builder.Services.AddScoped<ITransactionReconciliationStore>(provider =>
        provider.GetRequiredService<EfTransactionStore>());
    builder.Services.AddScoped<EfAttachmentMetadataStore>();
    builder.Services.AddScoped<IAttachmentMetadataStore>(provider =>
        provider.GetRequiredService<EfAttachmentMetadataStore>());
    builder.Services.AddScoped<IAttachmentMetadataReader>(provider =>
        provider.GetRequiredService<EfAttachmentMetadataStore>());
    builder.Services.AddScoped<IAttachmentLifecycleStore, EfAttachmentLifecycleStore>();
    builder.Services.AddScoped<EfCategoryStore>();
    builder.Services.AddScoped<ICategoryStore>(provider =>
        provider.GetRequiredService<EfCategoryStore>());
    builder.Services.AddScoped<ICategoryReader>(provider =>
        provider.GetRequiredService<EfCategoryStore>());
    builder.Services.AddScoped<ICategoryUpdater>(provider =>
        provider.GetRequiredService<EfCategoryStore>());
    builder.Services.AddScoped<ICategoryTransactionReassigner>(provider =>
        provider.GetRequiredService<EfCategoryStore>());
    builder.Services.AddScoped<ICategoryLifecycleStore>(provider =>
        provider.GetRequiredService<EfCategoryStore>());
    builder.Services.AddScoped<EfTagStore>();
    builder.Services.AddScoped<ITagStore>(provider => provider.GetRequiredService<EfTagStore>());
    builder.Services.AddScoped<ITagReader>(provider => provider.GetRequiredService<EfTagStore>());
    builder.Services.AddScoped<ITagUpdater>(provider => provider.GetRequiredService<EfTagStore>());
    builder.Services.AddScoped<ITagLifecycleStore>(provider =>
        provider.GetRequiredService<EfTagStore>());
    builder.Services.AddScoped<ITransactionTagStore>(provider =>
        provider.GetRequiredService<EfTagStore>());
    builder.Services.AddScoped<EfCounterpartyStore>();
    builder.Services.AddScoped<ICounterpartyStore>(provider =>
        provider.GetRequiredService<EfCounterpartyStore>());
    builder.Services.AddScoped<ICounterpartyReader>(provider =>
        provider.GetRequiredService<EfCounterpartyStore>());
    builder.Services.AddScoped<ICounterpartyUpdater>(provider =>
        provider.GetRequiredService<EfCounterpartyStore>());
    builder.Services.AddScoped<ICounterpartyLifecycleStore>(provider =>
        provider.GetRequiredService<EfCounterpartyStore>());
    builder.Services.AddScoped<ICounterpartyMerger>(provider =>
        provider.GetRequiredService<EfCounterpartyStore>());
    builder.Services.AddScoped<ICounterpartyCategorySuggester>(provider =>
        provider.GetRequiredService<EfCounterpartyStore>());
    builder.Services.AddScoped<EfBudgetStore>();
    builder.Services.AddScoped<IBudgetStore>(provider =>
        provider.GetRequiredService<EfBudgetStore>());
    builder.Services.AddScoped<IBudgetReader>(provider =>
        provider.GetRequiredService<EfBudgetStore>());
    builder.Services.AddScoped<IBudgetUpdater>(provider =>
        provider.GetRequiredService<EfBudgetStore>());
    builder.Services.AddScoped<IBudgetLifecycleStore>(provider =>
        provider.GetRequiredService<EfBudgetStore>());
    builder.Services.AddScoped<IBudgetConsumptionReader>(provider =>
        provider.GetRequiredService<EfBudgetStore>());
    builder.Services.AddScoped<EfGoalStore>();
    builder.Services.AddScoped<IGoalStore>(provider =>
        provider.GetRequiredService<EfGoalStore>());
    builder.Services.AddScoped<IGoalReader>(provider =>
        provider.GetRequiredService<EfGoalStore>());
    builder.Services.AddScoped<IGoalUpdater>(provider =>
        provider.GetRequiredService<EfGoalStore>());
    builder.Services.AddScoped<IGoalLifecycleStore>(provider =>
        provider.GetRequiredService<EfGoalStore>());
    builder.Services.AddScoped<IGoalProgressReader>(provider =>
        provider.GetRequiredService<EfGoalStore>());
    builder.Services.AddScoped<EfTransferStore>();
    builder.Services.AddScoped<ITransferStore>(provider =>
        provider.GetRequiredService<EfTransferStore>());
    builder.Services.AddScoped<ITransferReader>(provider =>
        provider.GetRequiredService<EfTransferStore>());
    builder.Services.AddScoped<ITransferLifecycleStore>(provider =>
        provider.GetRequiredService<EfTransferStore>());
    builder.Services.AddScoped<EfInstallmentPlanStore>();
    builder.Services.AddScoped<IInstallmentPlanStore>(provider =>
        provider.GetRequiredService<EfInstallmentPlanStore>());
    builder.Services.AddScoped<IInstallmentPlanReader>(provider =>
        provider.GetRequiredService<EfInstallmentPlanStore>());
    builder.Services.AddScoped<IInstallmentPlanLifecycleStore>(provider =>
        provider.GetRequiredService<EfInstallmentPlanStore>());
    builder.Services.AddScoped<EfRecurringTransactionStore>();
    builder.Services.AddScoped<IRecurringTransactionStore>(provider =>
        provider.GetRequiredService<EfRecurringTransactionStore>());
    builder.Services.AddScoped<IRecurringTransactionReader>(provider =>
        provider.GetRequiredService<EfRecurringTransactionStore>());
    builder.Services.AddScoped<IRecurringTransactionUpdater>(provider =>
        provider.GetRequiredService<EfRecurringTransactionStore>());
    builder.Services.AddScoped<IRecurringTransactionLifecycleStore>(provider =>
        provider.GetRequiredService<EfRecurringTransactionStore>());
    builder.Services.AddScoped<IRecurringTransactionMaterializer>(provider =>
        provider.GetRequiredService<EfRecurringTransactionStore>());
    builder.Services.AddScoped<EfInvestmentStore>();
    builder.Services.AddScoped<IInvestmentStore>(provider =>
        provider.GetRequiredService<EfInvestmentStore>());
    builder.Services.AddScoped<IInvestmentReader>(provider =>
        provider.GetRequiredService<EfInvestmentStore>());
    builder.Services.AddScoped<IInvestmentUpdater>(provider =>
        provider.GetRequiredService<EfInvestmentStore>());
    builder.Services.AddScoped<IInvestmentLifecycleStore>(provider =>
        provider.GetRequiredService<EfInvestmentStore>());
    builder.Services.AddScoped<IInvestmentMovementStore, EfInvestmentMovementStore>();
    builder.Services.AddScoped<IInvestmentValuationStore, EfInvestmentValuationStore>();
    builder.Services.AddScoped<EfConnectionStore>();
    builder.Services.AddScoped<IConnectionStore>(provider =>
        provider.GetRequiredService<EfConnectionStore>());
    builder.Services.AddScoped<IConnectionReader>(provider =>
        provider.GetRequiredService<EfConnectionStore>());
    builder.Services.AddScoped<IConnectionReauthenticationStore>(provider =>
        provider.GetRequiredService<EfConnectionStore>());
    builder.Services.AddScoped<IConnectionRevocationStore>(provider =>
        provider.GetRequiredService<EfConnectionStore>());
    builder.Services.AddScoped<IPluggySynchronizationStore, EfPluggySynchronizationStore>();
    builder.Services.AddScoped<IExcelImportStore, EfExcelImportStore>();
    builder.Services.AddSingleton<IExcelWorkbookParser, ExcelWorkbookParser>();
    builder.Services.AddScoped<IPdfInvoiceImportStore, EfPdfInvoiceImportStore>();
    builder.Services.AddSingleton<IPdfInvoiceParser, NubankPdfInvoiceParser>();
    builder.Services.AddScoped<IImportJobReader, EfImportJobReader>();
    builder.Services.AddScoped<ITableReportReader, EfTableReportReader>();
    builder.Services.AddScoped<ITransactionAggregationReader, EfTransactionAggregationReader>();
    builder.Services.AddSingleton<ITransactionDrillDownKeyCodec,
        DataProtectionTransactionDrillDownKeyCodec>();
    builder.Services.AddScoped<IImportJobRetryStore, EfImportJobRetryStore>();
    builder.Services.AddSingleton(new PaginationOptions(options.PageSizeMaximum));
    builder.Services.AddSingleton(new TransactionAggregationOptions(
        options.ReportMaximumRangeDays));
    builder.Services.AddSingleton(new TransactionDrillDownOptions(
        TimeSpan.FromMinutes(options.ReportKeyLifetimeMinutes)));
    builder.Services.AddSingleton(new ReconciliationOptions(
        options.ReconciliationAmountTolerance,
        options.ReconciliationDateToleranceDays));
    builder.Services.AddSingleton(new TagOptions(options.TransactionMaximumTags));
    builder.Services.AddSingleton(new ExcelImportOptions(options.ExcelImportMaximumFileBytes));
    builder.Services.AddSingleton(new PdfInvoiceImportOptions(
        options.PdfInvoiceImportMaximumFileBytes));
    builder.Services.AddSingleton(new AttachmentOptions(
        options.UploadMaximumBytes,
        options.UploadAllowedContentTypes));
    builder.Services.AddScoped<IBackgroundJobStore, EfBackgroundJobStore>();
    builder.Services.AddSingleton<IBackgroundJobQueue>(new BackgroundJobQueue(options.JobQueueCapacity));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<BackgroundJobProcessor>();
    builder.Services.AddScoped<IBackgroundJobHandler, RecurringMaterializationJobHandler>();
    builder.Services.AddScoped<IBackgroundJobHandler, PluggySynchronizationJobHandler>();
    builder.Services.AddScoped<IBackgroundJobHandler, ExcelImportJobHandler>();
    builder.Services.AddScoped<IBackgroundJobHandler, PdfInvoiceImportJobHandler>();
    builder.Services.AddHostedService<DatabaseInitializationHostedService>();
    builder.Services.AddHostedService<BackgroundJobHostedService>();
    builder.Services.AddHostedService<ExchangeRateSyncHostedService>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IRequestActorAccessor, HttpContextRequestActorAccessor>();
    builder.Services.AddSingleton(new UserProfileProvisioningOptions(
        options.DefaultDisplayCurrency,
        options.Locale));
    builder.Services.AddScoped<EfUserProfileStore>();
    builder.Services.AddScoped<IUserProfileReader>(provider =>
        provider.GetRequiredService<EfUserProfileStore>());
    builder.Services.AddScoped<IUserProfileProvisioner>(provider =>
        provider.GetRequiredService<EfUserProfileStore>());
    builder.Services.AddSingleton(new LocalAccountOptions(
        options.LocalAuthEnabled,
        options.LocalAuthRecoveryCodeCount,
        options.DefaultDisplayCurrency,
        options.Locale));
    builder.Services.AddSingleton(new RateSyncOptions(
        options.RatesSourceBaseUri,
        options.RatesSyncCron,
        options.RatesCurrencies));
    builder.Services.AddSingleton<IRateLimitDelay, RateLimitDelay>();
    builder.Services.AddHttpClient<IPtaxRateClient, PtaxRateClient>(client =>
        client.BaseAddress = options.RatesSourceBaseUri ?? new Uri("http://localhost/"));
    builder.Services.AddScoped<IBackgroundJobHandler, ExchangeRateSyncJobHandler>();
    builder.Services.AddScoped<ILocalAccountStore, EfLocalAccountStore>();
    builder.Services.AddSingleton<ILocalCredentialStoreAvailability, LocalCredentialStoreAvailability>();
    builder.Services.AddSingleton<ILocalRecoveryCodeGenerator, LocalRecoveryCodeGenerator>();
    builder.Services.AddScoped<IConnectionAccessTokenProtector, ConnectionAccessTokenProtector>();
    builder.Services.AddScoped<CommandMediator>();
    builder.Services.AddScoped<IValidator<CreateLocalAccountCommand>, CreateLocalAccountCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateLocalAccountCommand,
        CreateLocalAccountCommandOutput, CreateLocalAccountCommandHandler>();
    builder.Services.AddAuditedCommandHandler<AuthenticateLocalAccountCommand,
        AuthenticateLocalAccountCommandOutput, AuthenticateLocalAccountCommandHandler>();
    builder.Services.AddScoped<IValidator<RecoverLocalAccountCommand>, RecoverLocalAccountCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RecoverLocalAccountCommand,
        RecoverLocalAccountCommandOutput, RecoverLocalAccountCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RegenerateLocalAccountRecoveryCodesCommand,
        RegenerateLocalAccountRecoveryCodesCommandOutput, RegenerateLocalAccountRecoveryCodesCommandHandler>();
    builder.Services.AddAuditedCommandHandler<SynchronizeExchangeRatesCommand,
        SynchronizeExchangeRatesCommandOutput, SynchronizeExchangeRatesCommandHandler>();
    builder.Services.AddScoped<IValidator<RecordManualExchangeRateCommand>,
        RecordManualExchangeRateCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RecordManualExchangeRateCommand,
        RecordManualExchangeRateCommandOutput, RecordManualExchangeRateCommandHandler>();
    builder.Services.AddScoped<IValidator<CreateFinancialAccountCommand>,
        CreateFinancialAccountCommandValidator>();
    builder.Services.AddScoped<IValidator<ImportExcelWorkbookCommand>,
        ImportExcelWorkbookCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ImportExcelWorkbookCommand,
        ImportExcelWorkbookCommandOutput, ImportExcelWorkbookCommandHandler>();
    builder.Services.AddScoped<IValidator<ImportPdfInvoiceCommand>,
        ImportPdfInvoiceCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ImportPdfInvoiceCommand,
        ImportPdfInvoiceCommandOutput, ImportPdfInvoiceCommandHandler>();
    builder.Services.AddAuditedCommandHandler<CreateFinancialAccountCommand,
        CreateFinancialAccountCommandOutput, CreateFinancialAccountCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateFinancialAccountCommand>,
        UpdateFinancialAccountCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateFinancialAccountCommand,
        UpdateFinancialAccountCommandOutput, UpdateFinancialAccountCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteFinancialAccountCommand,
        FinancialAccountLifecycleCommandOutput, DeleteFinancialAccountCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreFinancialAccountCommand,
        FinancialAccountLifecycleCommandOutput, RestoreFinancialAccountCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteFinancialAccountCommand,
        FinancialAccountLifecycleCommandOutput, HardDeleteFinancialAccountCommandHandler>();
    builder.Services.AddScoped<IValidator<CreateCreditCardCommand>, CreateCreditCardCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateCreditCardCommand,
        CreateCreditCardCommandOutput, CreateCreditCardCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateCreditCardCommand>, UpdateCreditCardCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateCreditCardCommand,
        UpdateCreditCardCommandOutput, UpdateCreditCardCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteCreditCardCommand,
        CreditCardLifecycleCommandOutput, DeleteCreditCardCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreCreditCardCommand,
        CreditCardLifecycleCommandOutput, RestoreCreditCardCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteCreditCardCommand,
        CreditCardLifecycleCommandOutput, HardDeleteCreditCardCommandHandler>();
    builder.Services.AddScoped<IValidator<RecordTransactionCommand>,
        RecordTransactionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RecordTransactionCommand,
        RecordTransactionCommandOutput, RecordTransactionCommandHandler>();
    builder.Services.AddScoped<IValidator<AttachDocumentCommand>,
        AttachDocumentCommandValidator>();
    builder.Services.AddAuditedCommandHandler<AttachDocumentCommand,
        AttachDocumentCommandOutput, AttachDocumentCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteAttachmentCommand,
        AttachmentLifecycleCommandOutput, DeleteAttachmentCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteAttachmentCommand,
        AttachmentLifecycleCommandOutput, HardDeleteAttachmentCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateTransactionCommand>,
        UpdateTransactionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateTransactionCommand,
        UpdateTransactionCommandOutput, UpdateTransactionCommandHandler>();
    builder.Services.AddScoped<IValidator<ReconcileTransactionCommand>,
        ReconcileTransactionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ReconcileTransactionCommand,
        ReconcileTransactionCommandOutput, ReconcileTransactionCommandHandler>();
    builder.Services.AddScoped<IValidator<CreateCategoryCommand>, CreateCategoryCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateCategoryCommand,
        CreateCategoryCommandOutput, CreateCategoryCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateCategoryCommand>, UpdateCategoryCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateCategoryCommand,
        UpdateCategoryCommandOutput, UpdateCategoryCommandHandler>();
    builder.Services.AddScoped<IValidator<ReassignCategoryTransactionsCommand>,
        ReassignCategoryTransactionsCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ReassignCategoryTransactionsCommand,
        ReassignCategoryTransactionsCommandOutput,
        ReassignCategoryTransactionsCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteCategoryCommand,
        CategoryLifecycleCommandOutput, DeleteCategoryCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreCategoryCommand,
        CategoryLifecycleCommandOutput, RestoreCategoryCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteCategoryCommand,
        CategoryLifecycleCommandOutput, HardDeleteCategoryCommandHandler>();
    builder.Services.AddScoped<IValidator<CreateTagCommand>, CreateTagCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateTagCommand,
        TagCommandOutput, CreateTagCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateTagCommand>, UpdateTagCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateTagCommand,
        TagCommandOutput, UpdateTagCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteTagCommand,
        TagCommandOutput, DeleteTagCommandHandler>();
    builder.Services.AddScoped<IValidator<AttachTransactionTagCommand>,
        AttachTransactionTagCommandValidator>();
    builder.Services.AddAuditedCommandHandler<AttachTransactionTagCommand,
        TransactionTagCommandOutput, AttachTransactionTagCommandHandler>();
    builder.Services.AddScoped<IValidator<DetachTransactionTagCommand>,
        DetachTransactionTagCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DetachTransactionTagCommand,
        TransactionTagCommandOutput, DetachTransactionTagCommandHandler>();
    builder.Services.AddScoped<IValidator<CreateCounterpartyCommand>,
        CreateCounterpartyCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateCounterpartyCommand,
        CounterpartyCommandOutput, CreateCounterpartyCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateCounterpartyCommand>,
        UpdateCounterpartyCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateCounterpartyCommand,
        CounterpartyCommandOutput, UpdateCounterpartyCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteCounterpartyCommand,
        CounterpartyCommandOutput, DeleteCounterpartyCommandHandler>();
    builder.Services.AddScoped<IValidator<MergeCounterpartiesCommand>,
        MergeCounterpartiesCommandValidator>();
    builder.Services.AddAuditedCommandHandler<MergeCounterpartiesCommand,
        CounterpartyMergeCommandOutput, MergeCounterpartiesCommandHandler>();
    builder.Services.AddScoped<IValidator<CreateBudgetCommand>, CreateBudgetCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateBudgetCommand,
        BudgetCommandOutput, CreateBudgetCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateBudgetCommand>, UpdateBudgetCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateBudgetCommand,
        BudgetCommandOutput, UpdateBudgetCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteBudgetCommand,
        BudgetCommandOutput, DeleteBudgetCommandHandler>();
    builder.Services.AddScoped<IValidator<CreateGoalCommand>, CreateGoalCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateGoalCommand,
        GoalCommandOutput, CreateGoalCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateGoalCommand>, UpdateGoalCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateGoalCommand,
        GoalCommandOutput, UpdateGoalCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteGoalCommand,
        GoalCommandOutput, DeleteGoalCommandHandler>();
    builder.Services.AddScoped<IValidator<CreateConnectionCommand>,
        CreateConnectionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateConnectionCommand,
        CreateConnectionCommandOutput, CreateConnectionCommandHandler>();
    builder.Services.AddScoped<IValidator<ReauthenticateConnectionCommand>,
        ReauthenticateConnectionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ReauthenticateConnectionCommand,
        ReauthenticateConnectionCommandOutput, ReauthenticateConnectionCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RevokeConnectionCommand,
        RevokeConnectionCommandOutput, RevokeConnectionCommandHandler>();
    builder.Services.AddScoped<IValidator<SynchronizeConnectionCommand>,
        SynchronizeConnectionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<SynchronizeConnectionCommand,
        SynchronizeConnectionCommandOutput, SynchronizeConnectionCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RetryImportJobCommand,
        RetryImportJobCommandOutput, RetryImportJobCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteTransactionCommand,
        TransactionLifecycleCommandOutput, DeleteTransactionCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreTransactionCommand,
        TransactionLifecycleCommandOutput, RestoreTransactionCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteTransactionCommand,
        TransactionLifecycleCommandOutput, HardDeleteTransactionCommandHandler>();
    builder.Services.AddScoped<IValidator<RecordTransferCommand>,
        RecordTransferCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RecordTransferCommand,
        RecordTransferCommandOutput, RecordTransferCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteTransferCommand,
        TransferLifecycleCommandOutput, DeleteTransferCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreTransferCommand,
        TransferLifecycleCommandOutput, RestoreTransferCommandHandler>();
    builder.Services.AddScoped<IValidator<RecordInstallmentPlanCommand>,
        RecordInstallmentPlanCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RecordInstallmentPlanCommand,
        RecordInstallmentPlanCommandOutput, RecordInstallmentPlanCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteInstallmentPlanCommand,
        InstallmentPlanLifecycleCommandOutput, DeleteInstallmentPlanCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreInstallmentPlanCommand,
        InstallmentPlanLifecycleCommandOutput, RestoreInstallmentPlanCommandHandler>();
    builder.Services.AddScoped<IValidator<DefineRecurringTransactionCommand>,
        DefineRecurringTransactionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DefineRecurringTransactionCommand,
        DefineRecurringTransactionCommandOutput, DefineRecurringTransactionCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateRecurringTransactionCommand>,
        UpdateRecurringTransactionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateRecurringTransactionCommand,
        UpdateRecurringTransactionCommandOutput, UpdateRecurringTransactionCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteRecurringTransactionCommand,
        RecurringTransactionLifecycleCommandOutput, DeleteRecurringTransactionCommandHandler>();
    builder.Services.AddScoped<IValidator<MaterializeRecurringTransactionsCommand>,
        MaterializeRecurringTransactionsCommandValidator>();
    builder.Services.AddAuditedCommandHandler<MaterializeRecurringTransactionsCommand,
        MaterializeRecurringTransactionsCommandOutput, MaterializeRecurringTransactionsCommandHandler>();
    builder.Services.AddAuditedCommandHandler<CloseCreditCardStatementCommand,
        CloseCreditCardStatementCommandOutput, CloseCreditCardStatementCommandHandler>();
    builder.Services.AddScoped<IValidator<SettleCreditCardStatementCommand>,
        SettleCreditCardStatementCommandValidator>();
    builder.Services.AddAuditedCommandHandler<SettleCreditCardStatementCommand,
        SettleCreditCardStatementCommandOutput, SettleCreditCardStatementCommandHandler>();
    builder.Services.AddScoped<IValidator<CreateInvestmentCommand>,
        CreateInvestmentCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateInvestmentCommand,
        CreateInvestmentCommandOutput, CreateInvestmentCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateInvestmentCommand>,
        UpdateInvestmentCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateInvestmentCommand,
        UpdateInvestmentCommandOutput, UpdateInvestmentCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteInvestmentCommand,
        InvestmentLifecycleCommandOutput, DeleteInvestmentCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreInvestmentCommand,
        InvestmentLifecycleCommandOutput, RestoreInvestmentCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteInvestmentCommand,
        InvestmentLifecycleCommandOutput, HardDeleteInvestmentCommandHandler>();
    builder.Services.AddScoped<IValidator<RecordInvestmentMovementCommand>,
        RecordInvestmentMovementCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RecordInvestmentMovementCommand,
        RecordInvestmentMovementCommandOutput, RecordInvestmentMovementCommandHandler>();
    builder.Services.AddScoped<IValidator<RecordInvestmentValuationCommand>,
        RecordInvestmentValuationCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RecordInvestmentValuationCommand,
        RecordInvestmentValuationCommandOutput, RecordInvestmentValuationCommandHandler>();
    builder.Services.AddScoped<QueryMediator>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetMyProfileQuery, UserProfileOutput>,
        GetMyProfileQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetCategoryTreeQuery, CategoryTreeOutput>,
        GetCategoryTreeQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetCategoryByIdQuery, CategoryOutput>,
        GetCategoryByIdQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<ListTagsQuery, TagListOutput>,
        ListTagsQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<ListCounterpartiesQuery,
        CounterpartyListOutput>, ListCounterpartiesQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<SuggestCounterpartyCategoryQuery,
        CounterpartyCategorySuggestionOutput>, SuggestCounterpartyCategoryQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<ListBudgetsQuery, BudgetListOutput>,
        ListBudgetsQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetBudgetByIdQuery, BudgetOutput>,
        GetBudgetByIdQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetBudgetConsumptionQuery,
        BudgetConsumptionDetailOutput>, GetBudgetConsumptionQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<ListGoalsQuery, GoalListOutput>,
        ListGoalsQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetGoalByIdQuery, GoalOutput>,
        GetGoalByIdQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetGoalProgressQuery,
        GoalProgressDetailOutput>, GetGoalProgressQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<ListSupportedCurrenciesQuery,
        ListSupportedCurrenciesQueryOutput>, ListSupportedCurrenciesQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetCurrencyByCodeQuery, CurrencyOutput>,
        GetCurrencyByCodeQueryHandler>();
    builder.Services.AddScoped<IValidator<ConvertFigureQuery>, ConvertFigureQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<ConvertFigureQuery, ConvertFigureQueryOutput>,
        ConvertFigureQueryHandler>();
    builder.Services.AddScoped<IValidator<ListAuditEntriesQuery>, ListAuditEntriesQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListAuditEntriesQuery, AuditEntryOutput>,
        ListAuditEntriesQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetFinancialAccountByIdQuery, FinancialAccountOutput>,
        GetFinancialAccountByIdQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetFinancialAccountBalanceQuery,
        FinancialAccountBalanceOutput>, GetFinancialAccountBalanceQueryHandler>();
    builder.Services.AddScoped<IValidator<ListFinancialAccountsQuery>, ListFinancialAccountsQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListFinancialAccountsQuery, FinancialAccountOutput>,
        ListFinancialAccountsQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetCreditCardByIdQuery, CreditCardOutput>,
        GetCreditCardByIdQueryHandler>();
    builder.Services.AddScoped<IValidator<ListCreditCardsQuery>, ListCreditCardsQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListCreditCardsQuery, CreditCardOutput>,
        ListCreditCardsQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetCreditCardStatementByIdQuery,
        CreditCardStatementOutput>, GetCreditCardStatementByIdQueryHandler>();
    builder.Services.AddScoped<IValidator<ListCreditCardStatementsQuery>,
        ListCreditCardStatementsQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListCreditCardStatementsQuery,
        CreditCardStatementOutput>, ListCreditCardStatementsQueryHandler>();
    builder.Services.AddScoped<IValidator<GetInvestmentByIdQuery>, GetInvestmentByIdQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetInvestmentByIdQuery, InvestmentOutput>,
        GetInvestmentByIdQueryHandler>();
    builder.Services.AddScoped<IValidator<ListInvestmentsQuery>, ListInvestmentsQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListInvestmentsQuery, InvestmentOutput>,
        ListInvestmentsQueryHandler>();
    builder.Services.AddScoped<IValidator<ListInvestmentValuationsQuery>,
        ListInvestmentValuationsQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListInvestmentValuationsQuery,
        InvestmentValuationOutput>, ListInvestmentValuationsQueryHandler>();
    builder.Services.AddScoped<IValidator<GetTransactionByIdQuery>,
        GetTransactionByIdQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetTransactionByIdQuery, TransactionOutput>,
        GetTransactionByIdQueryHandler>();
    builder.Services.AddScoped<IValidator<DownloadAttachmentQuery>,
        DownloadAttachmentQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<DownloadAttachmentQuery,
        DownloadAttachmentQueryOutput>, DownloadAttachmentQueryHandler>();
    builder.Services.AddScoped<IValidator<SearchTransactionsQuery>,
        SearchTransactionsQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<SearchTransactionsQuery, TransactionSearchOutput>,
        SearchTransactionsQueryHandler>();
    builder.Services.AddScoped<IValidator<GetTransferByIdQuery>,
        GetTransferByIdQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetTransferByIdQuery, TransferOutput>,
        GetTransferByIdQueryHandler>();
    builder.Services.AddScoped<IValidator<GetInstallmentPlanByIdQuery>,
        GetInstallmentPlanByIdQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetInstallmentPlanByIdQuery,
        InstallmentPlanOutput>, GetInstallmentPlanByIdQueryHandler>();
    builder.Services.AddScoped<IValidator<GetRecurringTransactionByIdQuery>,
        GetRecurringTransactionByIdQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetRecurringTransactionByIdQuery,
        RecurringTransactionOutput>, GetRecurringTransactionByIdQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<ListDataSourcesQuery, DataSourceListOutput>,
        ListDataSourcesQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetConnectionByIdQuery, ConnectionOutput>,
        GetConnectionByIdQueryHandler>();
    builder.Services.AddScoped<IValidator<ListConnectionsQuery>, ListConnectionsQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListConnectionsQuery, ConnectionOutput>,
        ListConnectionsQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetImportJobByIdQuery, ImportJobOutput>,
        GetImportJobByIdQueryHandler>();
    builder.Services.AddScoped<IValidator<ListImportJobsQuery>, ListImportJobsQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListImportJobsQuery, ImportJobOutput>,
        ListImportJobsQueryHandler>();
    builder.Services.AddScoped<IValidator<ListImportedRecordsQuery>,
        ListImportedRecordsQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListImportedRecordsQuery,
        ImportedRecordOutput>, ListImportedRecordsQueryHandler>();
    builder.Services.AddScoped<IValidator<QueryRecordsAsTableQuery>,
        QueryRecordsAsTableQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<QueryRecordsAsTableQuery, TableReportOutput>,
        QueryRecordsAsTableQueryHandler>();
    builder.Services.AddScoped<IValidator<AggregateTransactionsQuery>,
        AggregateTransactionsQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<AggregateTransactionsQuery,
        TransactionAggregationOutput>, AggregateTransactionsQueryHandler>();
    builder.Services.AddScoped<IValidator<DrillIntoAggregationQuery>,
        DrillIntoAggregationQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<DrillIntoAggregationQuery,
        TransactionDrillDownOutput>, DrillIntoAggregationQueryHandler>();

    builder.Services.AddSingleton(new PluggySourceOptions(
        options.PluggyClientId,
        options.PluggyClientSecret,
        options.PluggyBaseUri,
        !options.LocalAuthEnabled));
    builder.Services.AddSingleton<IIngestionSource, PluggyIngestionSource>();
    builder.Services.AddHttpClient<IPluggyConnectionGateway, PluggyConnectionGateway>(client =>
        client.BaseAddress = options.PluggyBaseUri ?? new Uri("http://localhost/"));
    builder.Services.AddHttpClient<IPluggySynchronizationGateway, PluggySynchronizationGateway>(client =>
        client.BaseAddress = options.PluggyBaseUri ?? new Uri("http://localhost/"));
    builder.Services.AddSingleton<IIngestionSource, ExcelWorkbookIngestionSource>();
    builder.Services.AddSingleton<IIngestionSource, NubankInvoiceIngestionSource>();
    builder.Services.AddSingleton<IngestionSourceRegistry>();
    builder.Services.AddSingleton<IDataSourceCatalog>(provider =>
        provider.GetRequiredService<IngestionSourceRegistry>());
    RegisterAttachmentStore(builder.Services, options);

    builder.Services.AddControllers();
    var jwtConfiguration = BuildJwtConfiguration(options);
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(authentication =>
        {
            authentication.MapInboundClaims = false;
            authentication.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = jwtConfiguration.Keys.Select(key =>
                    new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key.Secret))),
                ValidateIssuer = true,
                ValidIssuer = options.AuthTokenIssuer,
                ValidateAudience = true,
                ValidAudience = options.AuthTokenAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });
    builder.Services.AddAuthorization(authorization =>
        authorization.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());
    builder.Services.AddSingleton(jwtConfiguration);
    builder.Services.AddSingleton<JwtHandler>();
    builder.Services.AddSingleton<FortunaIdentityMapper>();
    builder.Services.AddSingleton<ILocalAuthTokenIssuer, LocalAuthTokenIssuer>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(document =>
    {
        document.SwaggerDoc("v1", new()
        {
            Title = "Fortuna API",
            Version = "v1"
        });
        var jwtSecurityScheme = new OpenApiSecurityScheme
        {
            BearerFormat = "JWT",
            Name = "JWT Authentication",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = JwtBearerDefaults.AuthenticationScheme,
            Description = "Enter the Heimdall JWT bearer token."
        };
        var jwtRequirement = new OpenApiSecurityRequirement
        {
            { new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme), [] }
        };

        document.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, jwtSecurityScheme);
        document.AddSecurityRequirement(_ => jwtRequirement);
    });

    var app = builder.Build();
    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging(logging =>
        logging.GetLevel = (_, _, _) => LogEventLevel.Information);
    app.UseAuthentication();
    app.UseMiddleware<AuthenticatedActorMiddleware>();
    app.UseMiddleware<UserProfileProvisioningMiddleware>();
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

static JwtConfiguration BuildJwtConfiguration(FortunaOptions options)
{
    List<JwtKey> keys = [new("current", options.AuthTokenSecret)];

    if (!string.IsNullOrWhiteSpace(options.AuthPreviousTokenSecret) &&
        options.AuthPreviousTokenSecret != options.AuthTokenSecret)
    {
        keys.Add(new JwtKey("previous", options.AuthPreviousTokenSecret));
    }

    return new JwtConfiguration(
        options.AuthTokenExpirationInSeconds,
        options.AuthTokenIssuer,
        options.AuthTokenAudience,
        options.AuthTokenSecret,
        [])
    {
        Keys = keys
    };
}

public partial class Program;
