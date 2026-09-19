using ArturRios.Fortuna.Command.Auditing;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.WebApi.Services;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using FluentValidation;

namespace ArturRios.Fortuna.WebApi.Extensions;

public static class FortunaCommandsServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the command mediator, the write handlers with their validators and audit
    ///     decorator, and the services they use.
    /// </summary>
    public static IServiceCollection AddFortunaCommands(this IServiceCollection services)
    {
        services.AddScoped<IAuditEntryWriter, AuditEntryWriter>();
        services.AddScoped<DataExportBuilder>();
        services.AddSingleton<ILocalCredentialStoreAvailability, LocalCredentialStoreAvailability>();
        services.AddSingleton<ILocalRecoveryCodeGenerator, LocalRecoveryCodeGenerator>();
        services.AddScoped<IConnectionAccessTokenProtector, ConnectionAccessTokenProtector>();
        services.AddScoped<CommandMediator>();
        services.AddAuditedCommandHandler<LoginThroughApiCommand,
            LoginThroughApiCommandOutput, LoginThroughApiCommandHandler,
            LoginThroughApiCommandValidator>();
        services.AddAuditedCommandHandler<GoogleSignInThroughApiCommand,
            GoogleSignInThroughApiCommandOutput, GoogleSignInThroughApiCommandHandler,
            GoogleSignInThroughApiCommandValidator>();
        services.AddAuditedCommandHandler<VerifyTwoFactorThroughApiCommand,
            VerifyTwoFactorThroughApiCommandOutput, VerifyTwoFactorThroughApiCommandHandler,
            VerifyTwoFactorThroughApiCommandValidator>();
        services.AddAuditedCommandHandler<ResendTwoFactorChallengeCodeThroughApiCommand,
            ResendTwoFactorChallengeCodeThroughApiCommandOutput, ResendTwoFactorChallengeCodeThroughApiCommandHandler,
            ResendTwoFactorChallengeCodeThroughApiCommandValidator>();
        services.AddAuditedCommandHandler<GoogleSignOutThroughApiCommand,
            GoogleSignOutThroughApiCommandOutput, GoogleSignOutThroughApiCommandHandler>();
        services.AddAuditedCommandHandler<RequestPasswordRecoveryThroughApiCommand,
            RequestPasswordRecoveryThroughApiCommandOutput, RequestPasswordRecoveryThroughApiCommandHandler,
            RequestPasswordRecoveryThroughApiCommandValidator>();
        services.AddAuditedCommandHandler<ResetPasswordThroughApiCommand,
            ResetPasswordThroughApiCommandOutput, ResetPasswordThroughApiCommandHandler,
            ResetPasswordThroughApiCommandValidator>();
        services.AddAuditedCommandHandler<VerifyEmailThroughApiCommand,
            VerifyEmailThroughApiCommandOutput, VerifyEmailThroughApiCommandHandler,
            VerifyEmailThroughApiCommandValidator>();
        services.AddAuditedCommandHandler<ResendVerificationThroughApiCommand,
            ResendVerificationThroughApiCommandOutput, ResendVerificationThroughApiCommandHandler>();
        services.AddScoped<ICommandHandlerAsync<GetTwoFactorStatusThroughApiCommand,
            GetTwoFactorStatusThroughApiCommandOutput>, GetTwoFactorStatusThroughApiCommandHandler>();
        services.AddAuditedCommandHandler<EnableTwoFactorThroughApiCommand,
            EnableTwoFactorThroughApiCommandOutput, EnableTwoFactorThroughApiCommandHandler,
            EnableTwoFactorThroughApiCommandValidator>();
        services.AddAuditedCommandHandler<ConfirmTwoFactorThroughApiCommand,
            ConfirmTwoFactorThroughApiCommandOutput, ConfirmTwoFactorThroughApiCommandHandler,
            ConfirmTwoFactorThroughApiCommandValidator>();
        services.AddAuditedCommandHandler<DisableTwoFactorThroughApiCommand,
            DisableTwoFactorThroughApiCommandOutput, DisableTwoFactorThroughApiCommandHandler,
            DisableTwoFactorThroughApiCommandValidator>();
        services.AddAuditedCommandHandler<RegenerateRecoveryCodesThroughApiCommand,
            RegenerateRecoveryCodesThroughApiCommandOutput, RegenerateRecoveryCodesThroughApiCommandHandler,
            RegenerateRecoveryCodesThroughApiCommandValidator>();
        services.AddScoped<IValidator<CreateLocalAccountCommand>, CreateLocalAccountCommandValidator>();
        services.AddAuditedCommandHandler<EraseUserCommand,
            EraseUserCommandOutput, EraseUserCommandHandler,
            EraseUserCommandValidator>();
        services.AddAuditedCommandHandler<CreateLocalAccountCommand,
            CreateLocalAccountCommandOutput, CreateLocalAccountCommandHandler>();
        services.AddScoped<IValidator<AuthenticateLocalAccountCommand>,
            AuthenticateLocalAccountCommandValidator>();
        services.AddAuditedCommandHandler<AuthenticateLocalAccountCommand,
            AuthenticateLocalAccountCommandOutput, AuthenticateLocalAccountCommandHandler>();
        services.AddScoped<IValidator<RecoverLocalAccountCommand>, RecoverLocalAccountCommandValidator>();
        services.AddAuditedCommandHandler<RecoverLocalAccountCommand,
            RecoverLocalAccountCommandOutput, RecoverLocalAccountCommandHandler>();
        services.AddAuditedCommandHandler<RegenerateLocalAccountRecoveryCodesCommand,
            RegenerateLocalAccountRecoveryCodesCommandOutput, RegenerateLocalAccountRecoveryCodesCommandHandler>();
        services.AddAuditedCommandHandler<SynchronizeExchangeRatesCommand,
            SynchronizeExchangeRatesCommandOutput, SynchronizeExchangeRatesCommandHandler>();
        services.AddScoped<IValidator<RecordManualExchangeRateCommand>,
            RecordManualExchangeRateCommandValidator>();
        services.AddAuditedCommandHandler<RecordManualExchangeRateCommand,
            RecordManualExchangeRateCommandOutput, RecordManualExchangeRateCommandHandler>();
        services.AddAuditedCommandHandler<ImportExcelWorkbookCommand,
            ImportExcelWorkbookCommandOutput, ImportExcelWorkbookCommandHandler,
            ImportExcelWorkbookCommandValidator>();
        services.AddAuditedCommandHandler<ImportPdfInvoiceCommand,
            ImportPdfInvoiceCommandOutput, ImportPdfInvoiceCommandHandler,
            ImportPdfInvoiceCommandValidator>();
        services.AddAuditedCommandHandler<RequestDataExportCommand,
            RequestDataExportCommandOutput, RequestDataExportCommandHandler,
            RequestDataExportCommandValidator>();
        services.AddAuditedCommandHandler<RequestPersonalDataExportCommand,
            RequestPersonalDataExportCommandOutput, RequestPersonalDataExportCommandHandler>();
        services.AddAuditedCommandHandler<GrantProcessingConsentCommand,
            GrantProcessingConsentCommandOutput, GrantProcessingConsentCommandHandler,
            GrantProcessingConsentCommandValidator>();
        services.AddAuditedCommandHandler<WithdrawProcessingConsentCommand,
            WithdrawProcessingConsentCommandOutput, WithdrawProcessingConsentCommandHandler,
            WithdrawProcessingConsentCommandValidator>();
        services.AddAuditedCommandHandler<CreateFinancialAccountCommand,
            CreateFinancialAccountCommandOutput, CreateFinancialAccountCommandHandler,
            CreateFinancialAccountCommandValidator>();
        services.AddAuditedCommandHandler<UpdateFinancialAccountCommand,
            UpdateFinancialAccountCommandOutput, UpdateFinancialAccountCommandHandler,
            UpdateFinancialAccountCommandValidator>();
        services.AddAuditedCommandHandler<DeleteFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput, DeleteFinancialAccountCommandHandler>();
        services.AddAuditedCommandHandler<RestoreFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput, RestoreFinancialAccountCommandHandler>();
        services.AddAuditedCommandHandler<HardDeleteFinancialAccountCommand,
            FinancialAccountLifecycleCommandOutput, HardDeleteFinancialAccountCommandHandler>();
        services.AddAuditedCommandHandler<CreateCreditCardCommand,
            CreateCreditCardCommandOutput, CreateCreditCardCommandHandler,
            CreateCreditCardCommandValidator>();
        services.AddAuditedCommandHandler<UpdateCreditCardCommand,
            UpdateCreditCardCommandOutput, UpdateCreditCardCommandHandler,
            UpdateCreditCardCommandValidator>();
        services.AddAuditedCommandHandler<DeleteCreditCardCommand,
            CreditCardLifecycleCommandOutput, DeleteCreditCardCommandHandler>();
        services.AddAuditedCommandHandler<RestoreCreditCardCommand,
            CreditCardLifecycleCommandOutput, RestoreCreditCardCommandHandler>();
        services.AddAuditedCommandHandler<HardDeleteCreditCardCommand,
            CreditCardLifecycleCommandOutput, HardDeleteCreditCardCommandHandler>();
        services.AddAuditedCommandHandler<RecordTransactionCommand,
            RecordTransactionCommandOutput, RecordTransactionCommandHandler,
            RecordTransactionCommandValidator>();
        services.AddAuditedCommandHandler<AttachDocumentCommand,
            AttachDocumentCommandOutput, AttachDocumentCommandHandler,
            AttachDocumentCommandValidator>();
        services.AddAuditedCommandHandler<DeleteAttachmentCommand,
            AttachmentLifecycleCommandOutput, DeleteAttachmentCommandHandler>();
        services.AddAuditedCommandHandler<HardDeleteAttachmentCommand,
            AttachmentLifecycleCommandOutput, HardDeleteAttachmentCommandHandler>();
        services.AddAuditedCommandHandler<UpdateTransactionCommand,
            UpdateTransactionCommandOutput, UpdateTransactionCommandHandler,
            UpdateTransactionCommandValidator>();
        services.AddAuditedCommandHandler<ReconcileTransactionCommand,
            ReconcileTransactionCommandOutput, ReconcileTransactionCommandHandler,
            ReconcileTransactionCommandValidator>();
        services.AddAuditedCommandHandler<CreateCategoryCommand,
            CreateCategoryCommandOutput, CreateCategoryCommandHandler,
            CreateCategoryCommandValidator>();
        services.AddAuditedCommandHandler<UpdateCategoryCommand,
            UpdateCategoryCommandOutput, UpdateCategoryCommandHandler,
            UpdateCategoryCommandValidator>();
        services.AddAuditedCommandHandler<ReassignCategoryTransactionsCommand,
            ReassignCategoryTransactionsCommandOutput, ReassignCategoryTransactionsCommandHandler,
            ReassignCategoryTransactionsCommandValidator>();
        services.AddAuditedCommandHandler<DeleteCategoryCommand,
            CategoryLifecycleCommandOutput, DeleteCategoryCommandHandler>();
        services.AddAuditedCommandHandler<RestoreCategoryCommand,
            CategoryLifecycleCommandOutput, RestoreCategoryCommandHandler>();
        services.AddAuditedCommandHandler<HardDeleteCategoryCommand,
            CategoryLifecycleCommandOutput, HardDeleteCategoryCommandHandler>();
        services.AddAuditedCommandHandler<CreateTagCommand,
            TagCommandOutput, CreateTagCommandHandler,
            CreateTagCommandValidator>();
        services.AddAuditedCommandHandler<UpdateTagCommand,
            TagCommandOutput, UpdateTagCommandHandler,
            UpdateTagCommandValidator>();
        services.AddAuditedCommandHandler<DeleteTagCommand,
            TagCommandOutput, DeleteTagCommandHandler>();
        services.AddAuditedCommandHandler<AttachTransactionTagCommand,
            TransactionTagCommandOutput, AttachTransactionTagCommandHandler,
            AttachTransactionTagCommandValidator>();
        services.AddAuditedCommandHandler<DetachTransactionTagCommand,
            TransactionTagCommandOutput, DetachTransactionTagCommandHandler,
            DetachTransactionTagCommandValidator>();
        services.AddAuditedCommandHandler<CreateCounterpartyCommand,
            CounterpartyCommandOutput, CreateCounterpartyCommandHandler,
            CreateCounterpartyCommandValidator>();
        services.AddAuditedCommandHandler<UpdateCounterpartyCommand,
            CounterpartyCommandOutput, UpdateCounterpartyCommandHandler,
            UpdateCounterpartyCommandValidator>();
        services.AddAuditedCommandHandler<DeleteCounterpartyCommand,
            CounterpartyCommandOutput, DeleteCounterpartyCommandHandler>();
        services.AddAuditedCommandHandler<MergeCounterpartiesCommand,
            CounterpartyMergeCommandOutput, MergeCounterpartiesCommandHandler,
            MergeCounterpartiesCommandValidator>();
        services.AddAuditedCommandHandler<CreateBudgetCommand,
            BudgetCommandOutput, CreateBudgetCommandHandler,
            CreateBudgetCommandValidator>();
        services.AddAuditedCommandHandler<UpdateBudgetCommand,
            BudgetCommandOutput, UpdateBudgetCommandHandler,
            UpdateBudgetCommandValidator>();
        services.AddAuditedCommandHandler<DeleteBudgetCommand,
            BudgetCommandOutput, DeleteBudgetCommandHandler>();
        services.AddAuditedCommandHandler<CreateGoalCommand,
            GoalCommandOutput, CreateGoalCommandHandler,
            CreateGoalCommandValidator>();
        services.AddAuditedCommandHandler<UpdateGoalCommand,
            GoalCommandOutput, UpdateGoalCommandHandler,
            UpdateGoalCommandValidator>();
        services.AddAuditedCommandHandler<DeleteGoalCommand,
            GoalCommandOutput, DeleteGoalCommandHandler>();
        services.AddAuditedCommandHandler<CreateConnectionCommand,
            CreateConnectionCommandOutput, CreateConnectionCommandHandler,
            CreateConnectionCommandValidator>();
        services.AddAuditedCommandHandler<ReauthenticateConnectionCommand,
            ReauthenticateConnectionCommandOutput, ReauthenticateConnectionCommandHandler,
            ReauthenticateConnectionCommandValidator>();
        services.AddAuditedCommandHandler<RevokeConnectionCommand,
            RevokeConnectionCommandOutput, RevokeConnectionCommandHandler>();
        services.AddAuditedCommandHandler<SynchronizeConnectionCommand,
            SynchronizeConnectionCommandOutput, SynchronizeConnectionCommandHandler,
            SynchronizeConnectionCommandValidator>();
        services.AddAuditedCommandHandler<RetryImportJobCommand,
            RetryImportJobCommandOutput, RetryImportJobCommandHandler>();
        services.AddAuditedCommandHandler<DeleteTransactionCommand,
            TransactionLifecycleCommandOutput, DeleteTransactionCommandHandler>();
        services.AddAuditedCommandHandler<RestoreTransactionCommand,
            TransactionLifecycleCommandOutput, RestoreTransactionCommandHandler>();
        services.AddAuditedCommandHandler<HardDeleteTransactionCommand,
            TransactionLifecycleCommandOutput, HardDeleteTransactionCommandHandler>();
        services.AddAuditedCommandHandler<RecordTransferCommand,
            RecordTransferCommandOutput, RecordTransferCommandHandler,
            RecordTransferCommandValidator>();
        services.AddAuditedCommandHandler<DeleteTransferCommand,
            TransferLifecycleCommandOutput, DeleteTransferCommandHandler>();
        services.AddAuditedCommandHandler<RestoreTransferCommand,
            TransferLifecycleCommandOutput, RestoreTransferCommandHandler>();
        services.AddAuditedCommandHandler<RecordInstallmentPlanCommand,
            RecordInstallmentPlanCommandOutput, RecordInstallmentPlanCommandHandler,
            RecordInstallmentPlanCommandValidator>();
        services.AddAuditedCommandHandler<DeleteInstallmentPlanCommand,
            InstallmentPlanLifecycleCommandOutput, DeleteInstallmentPlanCommandHandler>();
        services.AddAuditedCommandHandler<RestoreInstallmentPlanCommand,
            InstallmentPlanLifecycleCommandOutput, RestoreInstallmentPlanCommandHandler>();
        services.AddAuditedCommandHandler<DefineRecurringTransactionCommand,
            DefineRecurringTransactionCommandOutput, DefineRecurringTransactionCommandHandler,
            DefineRecurringTransactionCommandValidator>();
        services.AddAuditedCommandHandler<UpdateRecurringTransactionCommand,
            UpdateRecurringTransactionCommandOutput, UpdateRecurringTransactionCommandHandler,
            UpdateRecurringTransactionCommandValidator>();
        services.AddAuditedCommandHandler<DeleteRecurringTransactionCommand,
            RecurringTransactionLifecycleCommandOutput, DeleteRecurringTransactionCommandHandler>();
        services.AddAuditedCommandHandler<MaterializeRecurringTransactionsCommand,
            MaterializeRecurringTransactionsCommandOutput, MaterializeRecurringTransactionsCommandHandler,
            MaterializeRecurringTransactionsCommandValidator>();
        services.AddAuditedCommandHandler<CloseCreditCardStatementCommand,
            CloseCreditCardStatementCommandOutput, CloseCreditCardStatementCommandHandler>();
        services.AddAuditedCommandHandler<SettleCreditCardStatementCommand,
            SettleCreditCardStatementCommandOutput, SettleCreditCardStatementCommandHandler,
            SettleCreditCardStatementCommandValidator>();
        services.AddAuditedCommandHandler<CreateInvestmentCommand,
            CreateInvestmentCommandOutput, CreateInvestmentCommandHandler,
            CreateInvestmentCommandValidator>();
        services.AddAuditedCommandHandler<UpdateInvestmentCommand,
            UpdateInvestmentCommandOutput, UpdateInvestmentCommandHandler,
            UpdateInvestmentCommandValidator>();
        services.AddAuditedCommandHandler<DeleteInvestmentCommand,
            InvestmentLifecycleCommandOutput, DeleteInvestmentCommandHandler>();
        services.AddAuditedCommandHandler<RestoreInvestmentCommand,
            InvestmentLifecycleCommandOutput, RestoreInvestmentCommandHandler>();
        services.AddAuditedCommandHandler<HardDeleteInvestmentCommand,
            InvestmentLifecycleCommandOutput, HardDeleteInvestmentCommandHandler>();
        services.AddAuditedCommandHandler<RecordInvestmentMovementCommand,
            RecordInvestmentMovementCommandOutput, RecordInvestmentMovementCommandHandler,
            RecordInvestmentMovementCommandValidator>();
        services.AddAuditedCommandHandler<RecordInvestmentValuationCommand,
            RecordInvestmentValuationCommandOutput, RecordInvestmentValuationCommandHandler,
            RecordInvestmentValuationCommandValidator>();

        return services;
    }
}
