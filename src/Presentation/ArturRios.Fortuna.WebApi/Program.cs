using Amazon.Runtime;
using Amazon.S3;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Attachments;
using ArturRios.Fortuna.Data.Accounts;
using ArturRios.Fortuna.Data.Auditing;
using ArturRios.Fortuna.Data.Cards;
using ArturRios.Fortuna.Data.Classification;
using ArturRios.Fortuna.Data.Currencies;
using ArturRios.Fortuna.Data.Exports;
using ArturRios.Fortuna.Data.Health;
using ArturRios.Fortuna.Data.Jobs;
using ArturRios.Fortuna.Data.Investments;
using ArturRios.Fortuna.Data.Ingestion;
using ArturRios.Fortuna.Data.Planning;
using ArturRios.Fortuna.Data.Projections;
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
using ArturRios.Fortuna.Integration.Exports;
using ArturRios.Fortuna.Integration.Rates;
using ArturRios.Fortuna.Integration.Storage;
using ArturRios.Fortuna.Integration.Security;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Investments;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Cards;
using ArturRios.Fortuna.Shared.Classification;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Health;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Planning;
using ArturRios.Fortuna.Shared.Projections;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Controllers;
using ArturRios.Fortuna.WebApi.Observability;
using ArturRios.Fortuna.WebApi.Output;
using ArturRios.Fortuna.WebApi.OpenApi;
using ArturRios.Fortuna.WebApi.Requests;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Fortuna.WebApi.Serialization;
using ArturRios.Fortuna.WebApi.Services;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Query.Validation;
using ArturRios.Jwt;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Mediator.Query;
using ArturRios.Mediator.Query.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;
using System.Text;
using System.Threading.RateLimiting;

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
    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton(new DatabaseDiagnosticsOptions(
        SensitiveDataLogging: false,
        DetailedErrors: !builder.Environment.IsProduction()));
    builder.Services.AddDbContext<AppDbContext>((services, database) => DatabaseProvider.Configure(
        database,
        options.DataDatabaseType,
        options.DataConnectionString));
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
    builder.Services.AddScoped<INetPositionReader, EfNetPositionReader>();
    builder.Services.AddScoped<ICashFlowProjectionReader, EfCashFlowProjectionReader>();
    builder.Services.AddScoped<ICommittedObligationReader, EfCommittedObligationReader>();
    builder.Services.AddScoped<EfDataExportStore>();
    builder.Services.AddScoped<IDataExportStore>(provider =>
        provider.GetRequiredService<EfDataExportStore>());
    builder.Services.AddScoped<IDataExportReader>(provider =>
        provider.GetRequiredService<EfDataExportStore>());
    builder.Services.AddScoped<IPersonalDataExportStore>(provider =>
        provider.GetRequiredService<EfDataExportStore>());
    builder.Services.AddScoped<IPersonalDataArchiveBuilder, EfPersonalDataArchiveBuilder>();
    builder.Services.AddSingleton<IDataExportRenderer, DataExportRenderer>();
    builder.Services.AddScoped<DataExportBuilder>();
    builder.Services.AddSingleton<ITransactionDrillDownKeyCodec,
        DataProtectionTransactionDrillDownKeyCodec>();
    builder.Services.AddScoped<IImportJobRetryStore, EfImportJobRetryStore>();
    builder.Services.AddSingleton(new PaginationOptions(options.PageSizeMaximum));
    builder.Services.AddSingleton(new TransactionAggregationOptions(
        options.ReportMaximumRangeDays));
    builder.Services.AddSingleton(new TransactionDrillDownOptions(
        TimeSpan.FromMinutes(options.ReportKeyLifetimeMinutes)));
    builder.Services.AddSingleton(new CashFlowProjectionOptions(
        options.ProjectionMaximumHorizonDays,
        90,
        30));
    builder.Services.AddSingleton(new DataExportOptions(
        options.ExportSynchronousThresholdRows,
        TimeSpan.FromHours(options.ExportRetentionHours),
        options.Locale));
    builder.Services.AddSingleton(new OperationalHealthOptions(
        TimeSpan.FromSeconds(options.HealthJobMaximumPendingSeconds)));
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
    builder.Services.AddSingleton<UploadLimits>();
    builder.Services.AddScoped<EfBackgroundJobStore>();
    builder.Services.AddScoped<IBackgroundJobStore>(provider =>
        provider.GetRequiredService<EfBackgroundJobStore>());
    builder.Services.AddScoped<IBackgroundJobHealthReader>(provider =>
        provider.GetRequiredService<EfBackgroundJobStore>());
    builder.Services.AddSingleton<IBackgroundJobQueue>(new BackgroundJobQueue(options.JobQueueCapacity));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<BackgroundJobProcessor>();
    builder.Services.AddScoped<IBackgroundJobHandler, RecurringMaterializationJobHandler>();
    builder.Services.AddScoped<IBackgroundJobHandler, PluggySynchronizationJobHandler>();
    builder.Services.AddScoped<IBackgroundJobHandler, ExcelImportJobHandler>();
    builder.Services.AddScoped<IBackgroundJobHandler, PdfInvoiceImportJobHandler>();
    builder.Services.AddScoped<IBackgroundJobHandler, DataExportJobHandler>();
    builder.Services.AddScoped<IBackgroundJobHandler, PersonalDataExportJobHandler>();
    builder.Services.AddHostedService<DatabaseInitializationHostedService>();
    builder.Services.AddHostedService<BackgroundJobHostedService>();
    builder.Services.AddHostedService<ExchangeRateSyncHostedService>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IRequestActorAccessor, HttpContextRequestActorAccessor>();
    builder.Services.AddScoped<ICurrentProfileResolver, CurrentProfileResolver>();
    builder.Services.AddSingleton(new UserProfileProvisioningOptions(
        options.DefaultDisplayCurrency,
        options.Locale));
    builder.Services.AddScoped<EfUserProfileStore>();
    builder.Services.AddScoped<IUserProfileReader>(provider =>
        provider.GetRequiredService<EfUserProfileStore>());
    builder.Services.AddScoped<IUserProfileProvisioner>(provider =>
        provider.GetRequiredService<EfUserProfileStore>());
    builder.Services.AddScoped<IUserErasureStore, EfUserErasureStore>();
    builder.Services.AddScoped<EfProcessingConsentStore>();
    builder.Services.AddScoped<IProcessingConsentStore>(provider =>
        provider.GetRequiredService<EfProcessingConsentStore>());
    builder.Services.AddScoped<IProcessingConsentReader>(provider =>
        provider.GetRequiredService<EfProcessingConsentStore>());
    builder.Services.AddSingleton(new ProcessingConsentOptions(
        options.ConsentExternalDataProcessingVersion));
    builder.Services.AddSingleton(new LocalAccountOptions(
        options.LocalAuthEnabled,
        options.LocalAuthRecoveryCodeCount,
        options.DefaultDisplayCurrency,
        options.Locale));
    builder.Services.AddSingleton(new HeimdallAuthOptions(options.HeimdallScopeId));
    builder.Services.AddHttpClient<IHeimdallAuthGateway, HeimdallAuthGateway>(client =>
    {
        client.BaseAddress = options.HeimdallBaseUri;
        client.Timeout = TimeSpan.FromSeconds(10);
    });
    builder.Services.AddSingleton(new RateSyncOptions(
        options.RatesSourceBaseUri,
        options.RatesSyncCron,
        options.RatesCurrencies));
    builder.Services.AddSingleton<IRateLimitDelay, RateLimitDelay>();
    builder.Services.AddHttpClient<IPtaxRateClient, PtaxRateClient>(client =>
    {
        client.BaseAddress = options.RatesSourceBaseUri ?? new Uri("http://localhost/");
        client.Timeout = HttpRetryPolicy.RequestTimeout;
    });
    builder.Services.AddScoped<IBackgroundJobHandler, ExchangeRateSyncJobHandler>();
    builder.Services.AddScoped<ILocalAccountStore, EfLocalAccountStore>();
    builder.Services.AddSingleton<ILocalCredentialStoreAvailability, LocalCredentialStoreAvailability>();
    builder.Services.AddSingleton<ILocalRecoveryCodeGenerator, LocalRecoveryCodeGenerator>();
    builder.Services.AddScoped<IConnectionAccessTokenProtector, ConnectionAccessTokenProtector>();
    builder.Services.AddScoped<CommandMediator>();
    builder.Services.AddAuditedCommandHandler<LoginThroughApiCommand,
        LoginThroughApiCommandOutput, LoginThroughApiCommandHandler,
        LoginThroughApiCommandValidator>();
    builder.Services.AddAuditedCommandHandler<GoogleSignInThroughApiCommand,
        GoogleSignInThroughApiCommandOutput, GoogleSignInThroughApiCommandHandler,
        GoogleSignInThroughApiCommandValidator>();
    builder.Services.AddAuditedCommandHandler<VerifyTwoFactorThroughApiCommand,
        VerifyTwoFactorThroughApiCommandOutput, VerifyTwoFactorThroughApiCommandHandler,
        VerifyTwoFactorThroughApiCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ResendTwoFactorChallengeCodeThroughApiCommand,
        ResendTwoFactorChallengeCodeThroughApiCommandOutput, ResendTwoFactorChallengeCodeThroughApiCommandHandler,
        ResendTwoFactorChallengeCodeThroughApiCommandValidator>();
    builder.Services.AddAuditedCommandHandler<GoogleSignOutThroughApiCommand,
        GoogleSignOutThroughApiCommandOutput, GoogleSignOutThroughApiCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RequestPasswordRecoveryThroughApiCommand,
        RequestPasswordRecoveryThroughApiCommandOutput, RequestPasswordRecoveryThroughApiCommandHandler,
        RequestPasswordRecoveryThroughApiCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ResetPasswordThroughApiCommand,
        ResetPasswordThroughApiCommandOutput, ResetPasswordThroughApiCommandHandler,
        ResetPasswordThroughApiCommandValidator>();
    builder.Services.AddAuditedCommandHandler<VerifyEmailThroughApiCommand,
        VerifyEmailThroughApiCommandOutput, VerifyEmailThroughApiCommandHandler,
        VerifyEmailThroughApiCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ResendVerificationThroughApiCommand,
        ResendVerificationThroughApiCommandOutput, ResendVerificationThroughApiCommandHandler>();
    builder.Services.AddScoped<ICommandHandlerAsync<GetTwoFactorStatusThroughApiCommand,
        GetTwoFactorStatusThroughApiCommandOutput>, GetTwoFactorStatusThroughApiCommandHandler>();
    builder.Services.AddAuditedCommandHandler<EnableTwoFactorThroughApiCommand,
        EnableTwoFactorThroughApiCommandOutput, EnableTwoFactorThroughApiCommandHandler,
        EnableTwoFactorThroughApiCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ConfirmTwoFactorThroughApiCommand,
        ConfirmTwoFactorThroughApiCommandOutput, ConfirmTwoFactorThroughApiCommandHandler,
        ConfirmTwoFactorThroughApiCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DisableTwoFactorThroughApiCommand,
        DisableTwoFactorThroughApiCommandOutput, DisableTwoFactorThroughApiCommandHandler,
        DisableTwoFactorThroughApiCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RegenerateRecoveryCodesThroughApiCommand,
        RegenerateRecoveryCodesThroughApiCommandOutput, RegenerateRecoveryCodesThroughApiCommandHandler,
        RegenerateRecoveryCodesThroughApiCommandValidator>();
    builder.Services.AddScoped<IValidator<CreateLocalAccountCommand>, CreateLocalAccountCommandValidator>();
    builder.Services.AddAuditedCommandHandler<EraseUserCommand,
        EraseUserCommandOutput, EraseUserCommandHandler,
        EraseUserCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateLocalAccountCommand,
        CreateLocalAccountCommandOutput, CreateLocalAccountCommandHandler>();
    builder.Services.AddScoped<IValidator<AuthenticateLocalAccountCommand>,
        AuthenticateLocalAccountCommandValidator>();
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
    builder.Services.AddAuditedCommandHandler<ImportExcelWorkbookCommand,
        ImportExcelWorkbookCommandOutput, ImportExcelWorkbookCommandHandler,
        ImportExcelWorkbookCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ImportPdfInvoiceCommand,
        ImportPdfInvoiceCommandOutput, ImportPdfInvoiceCommandHandler,
        ImportPdfInvoiceCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RequestDataExportCommand,
        RequestDataExportCommandOutput, RequestDataExportCommandHandler,
        RequestDataExportCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RequestPersonalDataExportCommand,
        RequestPersonalDataExportCommandOutput, RequestPersonalDataExportCommandHandler>();
    builder.Services.AddAuditedCommandHandler<GrantProcessingConsentCommand,
        GrantProcessingConsentCommandOutput, GrantProcessingConsentCommandHandler,
        GrantProcessingConsentCommandValidator>();
    builder.Services.AddAuditedCommandHandler<WithdrawProcessingConsentCommand,
        WithdrawProcessingConsentCommandOutput, WithdrawProcessingConsentCommandHandler,
        WithdrawProcessingConsentCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateFinancialAccountCommand,
        CreateFinancialAccountCommandOutput, CreateFinancialAccountCommandHandler,
        CreateFinancialAccountCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateFinancialAccountCommand,
        UpdateFinancialAccountCommandOutput, UpdateFinancialAccountCommandHandler,
        UpdateFinancialAccountCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteFinancialAccountCommand,
        FinancialAccountLifecycleCommandOutput, DeleteFinancialAccountCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreFinancialAccountCommand,
        FinancialAccountLifecycleCommandOutput, RestoreFinancialAccountCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteFinancialAccountCommand,
        FinancialAccountLifecycleCommandOutput, HardDeleteFinancialAccountCommandHandler>();
    builder.Services.AddAuditedCommandHandler<CreateCreditCardCommand,
        CreateCreditCardCommandOutput, CreateCreditCardCommandHandler,
        CreateCreditCardCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateCreditCardCommand,
        UpdateCreditCardCommandOutput, UpdateCreditCardCommandHandler,
        UpdateCreditCardCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteCreditCardCommand,
        CreditCardLifecycleCommandOutput, DeleteCreditCardCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreCreditCardCommand,
        CreditCardLifecycleCommandOutput, RestoreCreditCardCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteCreditCardCommand,
        CreditCardLifecycleCommandOutput, HardDeleteCreditCardCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RecordTransactionCommand,
        RecordTransactionCommandOutput, RecordTransactionCommandHandler,
        RecordTransactionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<AttachDocumentCommand,
        AttachDocumentCommandOutput, AttachDocumentCommandHandler,
        AttachDocumentCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteAttachmentCommand,
        AttachmentLifecycleCommandOutput, DeleteAttachmentCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteAttachmentCommand,
        AttachmentLifecycleCommandOutput, HardDeleteAttachmentCommandHandler>();
    builder.Services.AddAuditedCommandHandler<UpdateTransactionCommand,
        UpdateTransactionCommandOutput, UpdateTransactionCommandHandler,
        UpdateTransactionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ReconcileTransactionCommand,
        ReconcileTransactionCommandOutput, ReconcileTransactionCommandHandler,
        ReconcileTransactionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateCategoryCommand,
        CreateCategoryCommandOutput, CreateCategoryCommandHandler,
        CreateCategoryCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateCategoryCommand,
        UpdateCategoryCommandOutput, UpdateCategoryCommandHandler,
        UpdateCategoryCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ReassignCategoryTransactionsCommand,
        ReassignCategoryTransactionsCommandOutput, ReassignCategoryTransactionsCommandHandler,
        ReassignCategoryTransactionsCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteCategoryCommand,
        CategoryLifecycleCommandOutput, DeleteCategoryCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreCategoryCommand,
        CategoryLifecycleCommandOutput, RestoreCategoryCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteCategoryCommand,
        CategoryLifecycleCommandOutput, HardDeleteCategoryCommandHandler>();
    builder.Services.AddAuditedCommandHandler<CreateTagCommand,
        TagCommandOutput, CreateTagCommandHandler,
        CreateTagCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateTagCommand,
        TagCommandOutput, UpdateTagCommandHandler,
        UpdateTagCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteTagCommand,
        TagCommandOutput, DeleteTagCommandHandler>();
    builder.Services.AddAuditedCommandHandler<AttachTransactionTagCommand,
        TransactionTagCommandOutput, AttachTransactionTagCommandHandler,
        AttachTransactionTagCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DetachTransactionTagCommand,
        TransactionTagCommandOutput, DetachTransactionTagCommandHandler,
        DetachTransactionTagCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateCounterpartyCommand,
        CounterpartyCommandOutput, CreateCounterpartyCommandHandler,
        CreateCounterpartyCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateCounterpartyCommand,
        CounterpartyCommandOutput, UpdateCounterpartyCommandHandler,
        UpdateCounterpartyCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteCounterpartyCommand,
        CounterpartyCommandOutput, DeleteCounterpartyCommandHandler>();
    builder.Services.AddAuditedCommandHandler<MergeCounterpartiesCommand,
        CounterpartyMergeCommandOutput, MergeCounterpartiesCommandHandler,
        MergeCounterpartiesCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateBudgetCommand,
        BudgetCommandOutput, CreateBudgetCommandHandler,
        CreateBudgetCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateBudgetCommand,
        BudgetCommandOutput, UpdateBudgetCommandHandler,
        UpdateBudgetCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteBudgetCommand,
        BudgetCommandOutput, DeleteBudgetCommandHandler>();
    builder.Services.AddAuditedCommandHandler<CreateGoalCommand,
        GoalCommandOutput, CreateGoalCommandHandler,
        CreateGoalCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateGoalCommand,
        GoalCommandOutput, UpdateGoalCommandHandler,
        UpdateGoalCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteGoalCommand,
        GoalCommandOutput, DeleteGoalCommandHandler>();
    builder.Services.AddAuditedCommandHandler<CreateConnectionCommand,
        CreateConnectionCommandOutput, CreateConnectionCommandHandler,
        CreateConnectionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<ReauthenticateConnectionCommand,
        ReauthenticateConnectionCommandOutput, ReauthenticateConnectionCommandHandler,
        ReauthenticateConnectionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RevokeConnectionCommand,
        RevokeConnectionCommandOutput, RevokeConnectionCommandHandler>();
    builder.Services.AddAuditedCommandHandler<SynchronizeConnectionCommand,
        SynchronizeConnectionCommandOutput, SynchronizeConnectionCommandHandler,
        SynchronizeConnectionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RetryImportJobCommand,
        RetryImportJobCommandOutput, RetryImportJobCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DeleteTransactionCommand,
        TransactionLifecycleCommandOutput, DeleteTransactionCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreTransactionCommand,
        TransactionLifecycleCommandOutput, RestoreTransactionCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteTransactionCommand,
        TransactionLifecycleCommandOutput, HardDeleteTransactionCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RecordTransferCommand,
        RecordTransferCommandOutput, RecordTransferCommandHandler,
        RecordTransferCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteTransferCommand,
        TransferLifecycleCommandOutput, DeleteTransferCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreTransferCommand,
        TransferLifecycleCommandOutput, RestoreTransferCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RecordInstallmentPlanCommand,
        RecordInstallmentPlanCommandOutput, RecordInstallmentPlanCommandHandler,
        RecordInstallmentPlanCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteInstallmentPlanCommand,
        InstallmentPlanLifecycleCommandOutput, DeleteInstallmentPlanCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreInstallmentPlanCommand,
        InstallmentPlanLifecycleCommandOutput, RestoreInstallmentPlanCommandHandler>();
    builder.Services.AddAuditedCommandHandler<DefineRecurringTransactionCommand,
        DefineRecurringTransactionCommandOutput, DefineRecurringTransactionCommandHandler,
        DefineRecurringTransactionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateRecurringTransactionCommand,
        UpdateRecurringTransactionCommandOutput, UpdateRecurringTransactionCommandHandler,
        UpdateRecurringTransactionCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteRecurringTransactionCommand,
        RecurringTransactionLifecycleCommandOutput, DeleteRecurringTransactionCommandHandler>();
    builder.Services.AddAuditedCommandHandler<MaterializeRecurringTransactionsCommand,
        MaterializeRecurringTransactionsCommandOutput, MaterializeRecurringTransactionsCommandHandler,
        MaterializeRecurringTransactionsCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CloseCreditCardStatementCommand,
        CloseCreditCardStatementCommandOutput, CloseCreditCardStatementCommandHandler>();
    builder.Services.AddAuditedCommandHandler<SettleCreditCardStatementCommand,
        SettleCreditCardStatementCommandOutput, SettleCreditCardStatementCommandHandler,
        SettleCreditCardStatementCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateInvestmentCommand,
        CreateInvestmentCommandOutput, CreateInvestmentCommandHandler,
        CreateInvestmentCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateInvestmentCommand,
        UpdateInvestmentCommandOutput, UpdateInvestmentCommandHandler,
        UpdateInvestmentCommandValidator>();
    builder.Services.AddAuditedCommandHandler<DeleteInvestmentCommand,
        InvestmentLifecycleCommandOutput, DeleteInvestmentCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RestoreInvestmentCommand,
        InvestmentLifecycleCommandOutput, RestoreInvestmentCommandHandler>();
    builder.Services.AddAuditedCommandHandler<HardDeleteInvestmentCommand,
        InvestmentLifecycleCommandOutput, HardDeleteInvestmentCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RecordInvestmentMovementCommand,
        RecordInvestmentMovementCommandOutput, RecordInvestmentMovementCommandHandler,
        RecordInvestmentMovementCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RecordInvestmentValuationCommand,
        RecordInvestmentValuationCommandOutput, RecordInvestmentValuationCommandHandler,
        RecordInvestmentValuationCommandValidator>();
    builder.Services.AddScoped<QueryMediator>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetMyProfileQuery, UserProfileOutput>,
        GetMyProfileQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetMyProcessingConsentsQuery,
        ProcessingConsentQueryOutput>, GetMyProcessingConsentsQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetCategoryTreeQuery, CategoryTreeOutput>,
        GetCategoryTreeQueryHandler>();
    builder.Services.AddValidatedQueryHandler<GetCategoryByIdQuery,
        CategoryOutput, GetCategoryByIdQueryHandler,
        GetCategoryByIdQueryValidator>();
    builder.Services.AddValidatedQueryHandler<ListTagsQuery,
        TagListOutput, ListTagsQueryHandler,
        ListTagsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<ListCounterpartiesQuery,
        CounterpartyListOutput, ListCounterpartiesQueryHandler,
        ListCounterpartiesQueryValidator>();
    builder.Services.AddValidatedQueryHandler<SuggestCounterpartyCategoryQuery,
        CounterpartyCategorySuggestionOutput, SuggestCounterpartyCategoryQueryHandler,
        SuggestCounterpartyCategoryQueryValidator>();
    builder.Services.AddValidatedQueryHandler<ListBudgetsQuery,
        BudgetListOutput, ListBudgetsQueryHandler,
        ListBudgetsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetBudgetByIdQuery,
        BudgetOutput, GetBudgetByIdQueryHandler,
        GetBudgetByIdQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetBudgetConsumptionQuery,
        BudgetConsumptionDetailOutput, GetBudgetConsumptionQueryHandler,
        GetBudgetConsumptionQueryValidator>();
    builder.Services.AddValidatedQueryHandler<ListGoalsQuery,
        GoalListOutput, ListGoalsQueryHandler,
        ListGoalsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetGoalByIdQuery,
        GoalOutput, GetGoalByIdQueryHandler,
        GetGoalByIdQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetGoalProgressQuery,
        GoalProgressDetailOutput, GetGoalProgressQueryHandler,
        GetGoalProgressQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<ListSupportedCurrenciesQuery,
        ListSupportedCurrenciesQueryOutput>, ListSupportedCurrenciesQueryHandler>();
    builder.Services.AddValidatedQueryHandler<GetCurrencyByCodeQuery,
        CurrencyOutput, GetCurrencyByCodeQueryHandler,
        GetCurrencyByCodeQueryValidator>();
    builder.Services.AddValidatedQueryHandler<ConvertFigureQuery,
        ConvertFigureQueryOutput, ConvertFigureQueryHandler,
        ConvertFigureQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetNetPositionQuery,
        NetPositionOutput, GetNetPositionQueryHandler,
        GetNetPositionQueryValidator>();
    builder.Services.AddValidatedQueryHandler<ProjectCashFlowQuery,
        CashFlowProjectionOutput, ProjectCashFlowQueryHandler,
        ProjectCashFlowQueryValidator>();
    builder.Services.AddValidatedQueryHandler<ListCommittedObligationsQuery,
        CommittedObligationListOutput, ListCommittedObligationsQueryHandler,
        ListCommittedObligationsQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListAuditEntriesQuery,
        AuditEntryOutput, ListAuditEntriesQueryHandler,
        ListAuditEntriesQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetFinancialAccountByIdQuery,
        FinancialAccountOutput, GetFinancialAccountByIdQueryHandler,
        GetFinancialAccountByIdQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetFinancialAccountBalanceQuery,
        FinancialAccountBalanceOutput, GetFinancialAccountBalanceQueryHandler,
        GetFinancialAccountBalanceQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListFinancialAccountsQuery,
        FinancialAccountOutput, ListFinancialAccountsQueryHandler,
        ListFinancialAccountsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetCreditCardByIdQuery,
        CreditCardOutput, GetCreditCardByIdQueryHandler,
        GetCreditCardByIdQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListCreditCardsQuery,
        CreditCardOutput, ListCreditCardsQueryHandler,
        ListCreditCardsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetCreditCardStatementByIdQuery,
        CreditCardStatementOutput, GetCreditCardStatementByIdQueryHandler,
        GetCreditCardStatementByIdQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListCreditCardStatementsQuery,
        CreditCardStatementOutput, ListCreditCardStatementsQueryHandler,
        ListCreditCardStatementsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetInvestmentByIdQuery,
        InvestmentOutput, GetInvestmentByIdQueryHandler,
        GetInvestmentByIdQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListInvestmentsQuery,
        InvestmentOutput, ListInvestmentsQueryHandler,
        ListInvestmentsQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListInvestmentValuationsQuery,
        InvestmentValuationOutput, ListInvestmentValuationsQueryHandler,
        ListInvestmentValuationsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetTransactionByIdQuery,
        TransactionOutput, GetTransactionByIdQueryHandler,
        GetTransactionByIdQueryValidator>();
    builder.Services.AddValidatedQueryHandler<DownloadAttachmentQuery,
        DownloadAttachmentQueryOutput, DownloadAttachmentQueryHandler,
        DownloadAttachmentQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListTransactionAttachmentsQuery,
        AttachmentOutput, ListTransactionAttachmentsQueryHandler,
        ListTransactionAttachmentsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<SearchTransactionsQuery,
        TransactionSearchOutput, SearchTransactionsQueryHandler,
        SearchTransactionsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetTransferByIdQuery,
        TransferOutput, GetTransferByIdQueryHandler,
        GetTransferByIdQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetInstallmentPlanByIdQuery,
        InstallmentPlanOutput, GetInstallmentPlanByIdQueryHandler,
        GetInstallmentPlanByIdQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetRecurringTransactionByIdQuery,
        RecurringTransactionOutput, GetRecurringTransactionByIdQueryHandler,
        GetRecurringTransactionByIdQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListRecurringTransactionsQuery,
        RecurringTransactionOutput, ListRecurringTransactionsQueryHandler,
        ListRecurringTransactionsQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<ListDataSourcesQuery, DataSourceListOutput>,
        ListDataSourcesQueryHandler>();
    builder.Services.AddValidatedQueryHandler<GetConnectionByIdQuery,
        ConnectionOutput, GetConnectionByIdQueryHandler,
        GetConnectionByIdQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListConnectionsQuery,
        ConnectionOutput, ListConnectionsQueryHandler,
        ListConnectionsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetImportJobByIdQuery,
        ImportJobOutput, GetImportJobByIdQueryHandler,
        GetImportJobByIdQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetDataExportQuery,
        RetrieveDataExportQueryOutput, GetDataExportQueryHandler,
        GetDataExportQueryValidator>();
    builder.Services.AddValidatedQueryHandler<GetPersonalDataExportQuery,
        PersonalDataExportQueryOutput, GetPersonalDataExportQueryHandler,
        GetPersonalDataExportQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListImportJobsQuery,
        ImportJobOutput, ListImportJobsQueryHandler,
        ListImportJobsQueryValidator>();
    builder.Services.AddValidatedPaginatedQueryHandler<ListImportedRecordsQuery,
        ImportedRecordOutput, ListImportedRecordsQueryHandler,
        ListImportedRecordsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<QueryRecordsAsTableQuery,
        TableReportOutput, QueryRecordsAsTableQueryHandler,
        QueryRecordsAsTableQueryValidator>();
    builder.Services.AddValidatedQueryHandler<AggregateTransactionsQuery,
        TransactionAggregationOutput, AggregateTransactionsQueryHandler,
        AggregateTransactionsQueryValidator>();
    builder.Services.AddValidatedQueryHandler<DrillIntoAggregationQuery,
        TransactionDrillDownOutput, DrillIntoAggregationQueryHandler,
        DrillIntoAggregationQueryValidator>();

    builder.Services.AddSingleton(new PluggySourceOptions(
        options.PluggyClientId,
        options.PluggyClientSecret,
        options.PluggyBaseUri,
        !options.LocalAuthEnabled));
    builder.Services.AddSingleton<IIngestionSource, PluggyIngestionSource>();
    builder.Services.AddHttpClient<IPluggyConnectionGateway, PluggyConnectionGateway>(client =>
    {
        client.BaseAddress = options.PluggyBaseUri ?? new Uri("http://localhost/");
        client.Timeout = HttpRetryPolicy.RequestTimeout;
    });
    builder.Services.AddHttpClient<IPluggySynchronizationGateway, PluggySynchronizationGateway>(client =>
    {
        client.BaseAddress = options.PluggyBaseUri ?? new Uri("http://localhost/");
        client.Timeout = HttpRetryPolicy.RequestTimeout;
    });
    builder.Services.AddHttpClient(OperationalHealthClientNames.Aggregator, client =>
    {
        client.BaseAddress = options.PluggyBaseUri ?? new Uri("http://localhost/");
        client.Timeout = TimeSpan.FromSeconds(5);
    });
    builder.Services.AddHttpClient(OperationalHealthClientNames.ExchangeRateSource, client =>
    {
        client.BaseAddress = options.RatesSourceBaseUri ?? new Uri("http://localhost/");
        client.Timeout = TimeSpan.FromSeconds(5);
    });
    builder.Services.AddScoped<IOperationalHealthCheck, DatabaseOperationalHealthCheck>();
    builder.Services.AddScoped<IOperationalHealthCheck,
        AttachmentStorageOperationalHealthCheck>();
    builder.Services.AddScoped<IOperationalHealthCheck, JobRunnerOperationalHealthCheck>();
    builder.Services.AddScoped<IOperationalHealthCheck>(provider =>
        new ExternalServiceOperationalHealthCheck(
            "Aggregator",
            !options.LocalAuthEnabled &&
                !string.IsNullOrWhiteSpace(options.PluggyClientId) &&
                !string.IsNullOrWhiteSpace(options.PluggyClientSecret) &&
                options.PluggyBaseUri is not null,
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(
                OperationalHealthClientNames.Aggregator)));
    builder.Services.AddScoped<IOperationalHealthCheck>(provider =>
        new ExternalServiceOperationalHealthCheck(
            "ExchangeRateSource",
            !options.LocalAuthEnabled && options.RatesSourceBaseUri is not null,
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(
                OperationalHealthClientNames.ExchangeRateSource)));
    builder.Services.AddScoped<OperationalHealthEvaluator>();
    builder.Services.AddSingleton<IIngestionSource, ExcelWorkbookIngestionSource>();
    builder.Services.AddSingleton<IIngestionSource, NubankInvoiceIngestionSource>();
    builder.Services.AddSingleton<IngestionSourceRegistry>();
    builder.Services.AddSingleton<IDataSourceCatalog>(provider =>
        provider.GetRequiredService<IngestionSourceRegistry>());
    RegisterAttachmentStore(builder.Services, options);
    builder.Services.AddPrometheusMetrics(options.MetricsPort);

    builder.Services.AddControllers()
        .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new ExactDecimalJsonConverter()))
        .ConfigureApiBehaviorOptions(api =>
            api.InvalidModelStateResponseFactory = ApiErrorResponses.InvalidModelState);
    builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
        ForwardedHeadersSetup.Configure(forwarded, options));
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
    builder.Services.AddRateLimiter(rateLimiting =>
    {
        rateLimiting.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        rateLimiting.AddPolicy(AuthController.AnonymousRateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
    });
    builder.Services.AddSingleton(jwtConfiguration);
    builder.Services.AddSingleton<JwtHandler>();
    builder.Services.AddSingleton<FortunaIdentityMapper>();
    builder.Services.AddSingleton<ILocalAuthTokenIssuer, LocalAuthTokenIssuer>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(document =>
    {
        document.SchemaFilter<EnumNamesSchemaFilter>();
        document.MapType<decimal>(() => new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Format = "decimal",
            Pattern = @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$"
        });
        document.SwaggerDoc("v1", new()
        {
            Title = ApiContractMetadata.Service,
            Version = ApiContractMetadata.Version
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

    // Development keeps the developer exception page; elsewhere an unhandled exception becomes a
    // generic DataOutput 500 without internals. Forwarded headers are applied before anything
    // that reads the client address (request logging, the per-client rate limiter).
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler(handler => handler.Run(ApiErrorResponses.WriteUnexpectedErrorAsync));
    }

    app.UseForwardedHeaders();
    app.UsePrometheusMetrics(options.MetricsPort);
    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging(logging =>
        logging.GetLevel = (_, _, _) => LogEventLevel.Information);
    app.UseRateLimiter();
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
