using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Query.Validation;
using ArturRios.Mediator.Query;
using ArturRios.Mediator.Query.Interfaces;

namespace ArturRios.Fortuna.WebApi.Extensions;

public static class FortunaQueriesServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the query mediator and the read handlers with their validators.
    /// </summary>
    public static IServiceCollection AddFortunaQueries(this IServiceCollection services)
    {
        services.AddScoped<QueryMediator>();
        services.AddScoped<IQueryHandlerAsync<GetMyProfileQuery, UserProfileOutput>,
            GetMyProfileQueryHandler>();
        services.AddScoped<IQueryHandlerAsync<GetMyProcessingConsentsQuery,
            ProcessingConsentQueryOutput>, GetMyProcessingConsentsQueryHandler>();
        services.AddScoped<IQueryHandlerAsync<GetCategoryTreeQuery, CategoryTreeOutput>,
            GetCategoryTreeQueryHandler>();
        services.AddValidatedQueryHandler<GetCategoryByIdQuery,
            CategoryOutput, GetCategoryByIdQueryHandler,
            GetCategoryByIdQueryValidator>();
        services.AddValidatedQueryHandler<ListTagsQuery,
            TagListOutput, ListTagsQueryHandler,
            ListTagsQueryValidator>();
        services.AddValidatedQueryHandler<ListCounterpartiesQuery,
            CounterpartyListOutput, ListCounterpartiesQueryHandler,
            ListCounterpartiesQueryValidator>();
        services.AddValidatedQueryHandler<SuggestCounterpartyCategoryQuery,
            CounterpartyCategorySuggestionOutput, SuggestCounterpartyCategoryQueryHandler,
            SuggestCounterpartyCategoryQueryValidator>();
        services.AddValidatedQueryHandler<ListBudgetsQuery,
            BudgetListOutput, ListBudgetsQueryHandler,
            ListBudgetsQueryValidator>();
        services.AddValidatedQueryHandler<GetBudgetByIdQuery,
            BudgetOutput, GetBudgetByIdQueryHandler,
            GetBudgetByIdQueryValidator>();
        services.AddValidatedQueryHandler<GetBudgetConsumptionQuery,
            BudgetConsumptionDetailOutput, GetBudgetConsumptionQueryHandler,
            GetBudgetConsumptionQueryValidator>();
        services.AddValidatedQueryHandler<ListGoalsQuery,
            GoalListOutput, ListGoalsQueryHandler,
            ListGoalsQueryValidator>();
        services.AddValidatedQueryHandler<GetGoalByIdQuery,
            GoalOutput, GetGoalByIdQueryHandler,
            GetGoalByIdQueryValidator>();
        services.AddValidatedQueryHandler<GetGoalProgressQuery,
            GoalProgressDetailOutput, GetGoalProgressQueryHandler,
            GetGoalProgressQueryValidator>();
        services.AddScoped<IQueryHandlerAsync<ListSupportedCurrenciesQuery,
            ListSupportedCurrenciesQueryOutput>, ListSupportedCurrenciesQueryHandler>();
        services.AddValidatedQueryHandler<GetCurrencyByCodeQuery,
            CurrencyOutput, GetCurrencyByCodeQueryHandler,
            GetCurrencyByCodeQueryValidator>();
        services.AddValidatedQueryHandler<ConvertFigureQuery,
            ConvertFigureQueryOutput, ConvertFigureQueryHandler,
            ConvertFigureQueryValidator>();
        services.AddValidatedQueryHandler<GetNetPositionQuery,
            NetPositionOutput, GetNetPositionQueryHandler,
            GetNetPositionQueryValidator>();
        services.AddValidatedQueryHandler<ProjectCashFlowQuery,
            CashFlowProjectionOutput, ProjectCashFlowQueryHandler,
            ProjectCashFlowQueryValidator>();
        services.AddValidatedQueryHandler<ListCommittedObligationsQuery,
            CommittedObligationListOutput, ListCommittedObligationsQueryHandler,
            ListCommittedObligationsQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListAuditEntriesQuery,
            AuditEntryOutput, ListAuditEntriesQueryHandler,
            ListAuditEntriesQueryValidator>();
        services.AddValidatedQueryHandler<GetFinancialAccountByIdQuery,
            FinancialAccountOutput, GetFinancialAccountByIdQueryHandler,
            GetFinancialAccountByIdQueryValidator>();
        services.AddValidatedQueryHandler<GetFinancialAccountBalanceQuery,
            FinancialAccountBalanceOutput, GetFinancialAccountBalanceQueryHandler,
            GetFinancialAccountBalanceQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListFinancialAccountsQuery,
            FinancialAccountOutput, ListFinancialAccountsQueryHandler,
            ListFinancialAccountsQueryValidator>();
        services.AddValidatedQueryHandler<GetCreditCardByIdQuery,
            CreditCardOutput, GetCreditCardByIdQueryHandler,
            GetCreditCardByIdQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListCreditCardsQuery,
            CreditCardOutput, ListCreditCardsQueryHandler,
            ListCreditCardsQueryValidator>();
        services.AddValidatedQueryHandler<GetCreditCardStatementByIdQuery,
            CreditCardStatementOutput, GetCreditCardStatementByIdQueryHandler,
            GetCreditCardStatementByIdQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListCreditCardStatementsQuery,
            CreditCardStatementOutput, ListCreditCardStatementsQueryHandler,
            ListCreditCardStatementsQueryValidator>();
        services.AddValidatedQueryHandler<GetInvestmentByIdQuery,
            InvestmentOutput, GetInvestmentByIdQueryHandler,
            GetInvestmentByIdQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListInvestmentsQuery,
            InvestmentOutput, ListInvestmentsQueryHandler,
            ListInvestmentsQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListInvestmentValuationsQuery,
            InvestmentValuationOutput, ListInvestmentValuationsQueryHandler,
            ListInvestmentValuationsQueryValidator>();
        services.AddValidatedQueryHandler<GetTransactionByIdQuery,
            TransactionOutput, GetTransactionByIdQueryHandler,
            GetTransactionByIdQueryValidator>();
        services.AddValidatedQueryHandler<DownloadAttachmentQuery,
            DownloadAttachmentQueryOutput, DownloadAttachmentQueryHandler,
            DownloadAttachmentQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListTransactionAttachmentsQuery,
            AttachmentOutput, ListTransactionAttachmentsQueryHandler,
            ListTransactionAttachmentsQueryValidator>();
        services.AddValidatedQueryHandler<SearchTransactionsQuery,
            TransactionSearchOutput, SearchTransactionsQueryHandler,
            SearchTransactionsQueryValidator>();
        services.AddValidatedQueryHandler<GetTransferByIdQuery,
            TransferOutput, GetTransferByIdQueryHandler,
            GetTransferByIdQueryValidator>();
        services.AddValidatedQueryHandler<GetInstallmentPlanByIdQuery,
            InstallmentPlanOutput, GetInstallmentPlanByIdQueryHandler,
            GetInstallmentPlanByIdQueryValidator>();
        services.AddValidatedQueryHandler<GetRecurringTransactionByIdQuery,
            RecurringTransactionOutput, GetRecurringTransactionByIdQueryHandler,
            GetRecurringTransactionByIdQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListRecurringTransactionsQuery,
            RecurringTransactionOutput, ListRecurringTransactionsQueryHandler,
            ListRecurringTransactionsQueryValidator>();
        services.AddScoped<IQueryHandlerAsync<ListDataSourcesQuery, DataSourceListOutput>,
            ListDataSourcesQueryHandler>();
        services.AddValidatedQueryHandler<GetConnectionByIdQuery,
            ConnectionOutput, GetConnectionByIdQueryHandler,
            GetConnectionByIdQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListConnectionsQuery,
            ConnectionOutput, ListConnectionsQueryHandler,
            ListConnectionsQueryValidator>();
        services.AddValidatedQueryHandler<GetImportJobByIdQuery,
            ImportJobOutput, GetImportJobByIdQueryHandler,
            GetImportJobByIdQueryValidator>();
        services.AddValidatedQueryHandler<GetDataExportQuery,
            RetrieveDataExportQueryOutput, GetDataExportQueryHandler,
            GetDataExportQueryValidator>();
        services.AddValidatedQueryHandler<GetPersonalDataExportQuery,
            PersonalDataExportQueryOutput, GetPersonalDataExportQueryHandler,
            GetPersonalDataExportQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListImportJobsQuery,
            ImportJobOutput, ListImportJobsQueryHandler,
            ListImportJobsQueryValidator>();
        services.AddValidatedPaginatedQueryHandler<ListImportedRecordsQuery,
            ImportedRecordOutput, ListImportedRecordsQueryHandler,
            ListImportedRecordsQueryValidator>();
        services.AddValidatedQueryHandler<QueryRecordsAsTableQuery,
            TableReportOutput, QueryRecordsAsTableQueryHandler,
            QueryRecordsAsTableQueryValidator>();
        services.AddValidatedQueryHandler<AggregateTransactionsQuery,
            TransactionAggregationOutput, AggregateTransactionsQueryHandler,
            AggregateTransactionsQueryValidator>();
        services.AddValidatedQueryHandler<DrillIntoAggregationQuery,
            TransactionDrillDownOutput, DrillIntoAggregationQueryHandler,
            DrillIntoAggregationQueryValidator>();

        return services;
    }
}
