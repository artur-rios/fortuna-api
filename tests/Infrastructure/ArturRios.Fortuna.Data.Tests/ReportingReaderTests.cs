using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Reporting;
using ArturRios.Fortuna.Data.Transactions;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Util.Test.Attributes;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Tests;

/// <summary>
/// Runs every reporting read path against one seeded scenario. Each database provider derives
/// from this class, so the same expectations hold on PostgreSQL and on SQLite.
/// </summary>
public abstract class ReportingReaderTests
{
    private ReportingScenario? scenario;

    private protected ReportingScenario Scenario => scenario!;

    protected abstract AppDbContext CreateContext();

    protected async Task SeedAsync()
    {
        await using var context = CreateContext();
        scenario = await ReportingScenario.SeedAsync(context);
    }

    [FunctionalFact]
    public async Task GivenRowsUnderDeletedParents_WhenReadByEveryPath_ThenCountsAndTotalsAgree()
    {
        await using var context = CreateContext();
        var criteria = new TransactionSearchCriteria
        {
            UserId = Scenario.UserId,
            From = ReportingScenario.From,
            To = ReportingScenario.To
        };
        var store = new EfTransactionStore(context, null!);

        var figures = await AggregateAsync(context, AggregationDimension.Category);
        var drillCount = await store.Query(criteria).CountAsync(item => !item.IsTransfer);
        var totals = Assert.Single(await store.SummarizeAsync(criteria, CancellationToken.None));
        var table = await ReadTableAsync(context, "transactions", ["id"]);

        Assert.Equal(ReportingScenario.ReportableCount, figures.Sum(item => item.RecordCount));
        Assert.Equal(ReportingScenario.ReportableCount, drillCount);
        Assert.Equal(ReportingScenario.Earning - ReportingScenario.Expense,
            figures.Sum(item => item.Amount));
        Assert.Equal("BRL", totals.CurrencyCode);
        Assert.Equal(ReportingScenario.Expense, totals.Expense);
        Assert.Equal(ReportingScenario.Earning, totals.Earning);
        Assert.Equal(ReportingScenario.ReportableCount + 2, table.TotalCount);
    }

    [FunctionalFact]
    public async Task GivenDeletedRowsRequested_WhenSummarized_ThenTotalsStillExcludeThem()
    {
        await using var context = CreateContext();
        var store = new EfTransactionStore(context, null!);
        var criteria = new TransactionSearchCriteria
        {
            UserId = Scenario.UserId,
            IncludeDeleted = true
        };

        var listed = await store.Query(criteria).CountAsync();
        var totals = Assert.Single(await store.SummarizeAsync(criteria, CancellationToken.None));

        Assert.Equal(8, listed);
        Assert.Equal(ReportingScenario.Expense, totals.Expense);
        Assert.Equal(ReportingScenario.Earning, totals.Earning);
    }

    protected Task<IReadOnlyCollection<TransactionAggregationFigureSnapshot>> AggregateAsync(
        AppDbContext context,
        AggregationDimension dimension,
        AggregationGranularity? granularity = null,
        bool rollup = false,
        string? text = null,
        decimal? minimumAmount = null,
        decimal? maximumAmount = null,
        Guid? tagId = null,
        IReadOnlyCollection<TransactionAggregationSelection>? selections = null) =>
        new EfTransactionAggregationReader(context).ReadAsync(
            new TransactionAggregationCriteria(
                Scenario.UserId,
                dimension,
                granularity,
                ReportingScenario.From,
                ReportingScenario.To,
                rollup,
                null,
                null,
                null,
                tagId,
                null,
                null,
                minimumAmount,
                maximumAmount,
                text,
                selections ?? []),
            CancellationToken.None);

    protected async Task<TableReportSnapshot> ReadTableAsync(
        AppDbContext context,
        string recordSet,
        IReadOnlyCollection<string> columns,
        IReadOnlyCollection<TableFilterCriteria>? filters = null,
        IReadOnlyCollection<TableSortCriteria>? sorts = null,
        int pageNumber = 1,
        int pageSize = 50)
    {
        var result = await new EfTableReportReader(context).ReadAsync(
            new TableReportCriteria(
                Scenario.UserId,
                recordSet,
                columns,
                filters ?? [],
                sorts ?? [],
                pageNumber,
                pageSize),
            CancellationToken.None);

        Assert.Equal(TableReportReadOutcome.Succeeded, result.Outcome);

        return result.Report!;
    }
}
