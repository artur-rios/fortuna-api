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
public abstract class ReportingReaderTests(ReportingDatabaseFixture fixture)
{
    private protected ReportingScenario Scenario => fixture.Scenario;

    private AppDbContext CreateContext() => fixture.CreateContext();

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

    [FunctionalTheory]
    [InlineData("50%", 1)]
    [InlineData("_", 1)]
    [InlineData("SALARY", 1)]
    [InlineData("%", 1)]
    [InlineData("no such text", 0)]
    public async Task GivenLikeWildcardsInText_WhenAggregated_ThenTheyMatchLiterally(
        string text,
        int expected)
    {
        await using var context = CreateContext();

        var figures = await AggregateAsync(context, AggregationDimension.Category, text: text);

        Assert.Equal(expected, figures.Sum(item => item.RecordCount));
    }

    [FunctionalFact]
    public async Task GivenCategoryRollup_WhenAggregatedAndSelected_ThenDescendantsRollIntoRoot()
    {
        await using var context = CreateContext();

        var figures = await AggregateAsync(context, AggregationDimension.Category, rollup: true);
        var selected = await AggregateAsync(
            context,
            AggregationDimension.Category,
            selections: [new TransactionAggregationSelection(
                AggregationDimension.Category,
                Scenario.FoodId.ToString(),
                true)]);

        var food = Assert.Single(figures.GroupBy(item => item.DimensionValue));
        Assert.Equal(Scenario.FoodId.ToString(), food.Key);
        Assert.Equal("Food", food.First().Label);
        Assert.Equal(ReportingScenario.ReportableCount, food.Sum(item => item.RecordCount));
        Assert.Equal(ReportingScenario.ReportableCount, selected.Sum(item => item.RecordCount));
        Assert.Contains(selected, item => item.DimensionValue == Scenario.GroceriesId.ToString());
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
