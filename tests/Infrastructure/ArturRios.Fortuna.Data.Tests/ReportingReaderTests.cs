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

    [FunctionalTheory]
    [InlineData(AggregationGranularity.Day, "2026-09-02,2026-09-10,2026-09-14,2026-09-29")]
    [InlineData(AggregationGranularity.Week, "2026-08-31,2026-09-07,2026-09-14,2026-09-28")]
    [InlineData(AggregationGranularity.Month, "2026-09-01")]
    [InlineData(AggregationGranularity.Quarter, "2026-07-01")]
    [InlineData(AggregationGranularity.Year, "2026-01-01")]
    public async Task GivenGranularity_WhenAggregatedByPeriod_ThenBucketsStartOnPeriodBoundaries(
        AggregationGranularity granularity,
        string expectedStarts)
    {
        await using var context = CreateContext();

        var figures = await AggregateAsync(context, AggregationDimension.Period, granularity);

        Assert.Equal(expectedStarts.Split(','), figures
            .Select(item => item.BucketStart!.Value.ToString("yyyy-MM-dd"))
            .Distinct());
        Assert.All(figures, item =>
        {
            Assert.Equal(item.BucketStart!.Value.ToString("yyyy-MM-dd"), item.DimensionValue);
            Assert.Equal(item.DimensionValue, item.Label);
            Assert.Equal("BRL", item.CurrencyCode);
        });
        Assert.Equal(ReportingScenario.Earning - ReportingScenario.Expense,
            figures.Sum(item => item.Amount));
    }

    [FunctionalFact]
    public async Task GivenEachDimension_WhenAggregated_ThenBucketsUseLowercaseIdsAndExactAmounts()
    {
        await using var context = CreateContext();

        var category = await AggregateAsync(context, AggregationDimension.Category);
        var account = await AggregateAsync(context, AggregationDimension.Account);
        var card = await AggregateAsync(context, AggregationDimension.Card);
        var counterparty = await AggregateAsync(context, AggregationDimension.Counterparty);
        var tag = await AggregateAsync(context, AggregationDimension.Tag);

        Assert.Equal(91.75m, Bucket(category, Scenario.FoodId).Sum(item => item.Amount));
        Assert.Equal(-10.50m, Bucket(category, Scenario.GroceriesId).Sum(item => item.Amount));
        Assert.Equal(3, Bucket(account, Scenario.CheckingId).Sum(item => item.RecordCount));
        Assert.Single(account.GroupBy(item => item.DimensionValue));
        Assert.Equal(-5.25m, Assert.Single(Bucket(card, Scenario.CardId)).Amount);
        Assert.Equal(1, Bucket(counterparty, Scenario.CounterpartyId).Sum(item => item.RecordCount));
        Assert.Equal(3, counterparty
            .Where(item => item.DimensionValue == "none")
            .Sum(item => item.RecordCount));
        Assert.Equal("Live", Assert.Single(Bucket(tag, Scenario.LiveTagId)).Label);
        Assert.Single(tag);
    }

    [FunctionalFact]
    public async Task GivenFiltersAndSelections_WhenAggregated_ThenDecimalAndKeyRulesApply()
    {
        await using var context = CreateContext();

        var minimum = await AggregateAsync(
            context, AggregationDimension.Category, minimumAmount: 5.25m);
        var maximum = await AggregateAsync(
            context, AggregationDimension.Category, maximumAmount: 10m);
        var liveTag = await AggregateAsync(
            context, AggregationDimension.Category, tagId: Scenario.LiveTagId);
        var deletedTag = await AggregateAsync(
            context, AggregationDimension.Category, tagId: Scenario.DeletedTagId);
        var selected = await AggregateAsync(
            context,
            AggregationDimension.Category,
            selections:
            [
                new TransactionAggregationSelection(
                    AggregationDimension.Period,
                    "2026-09-10",
                    false,
                    new DateOnly(2026, 9, 10),
                    new DateOnly(2026, 9, 29)),
                new TransactionAggregationSelection(
                    AggregationDimension.Counterparty,
                    "none",
                    false),
                new TransactionAggregationSelection(
                    AggregationDimension.Account,
                    Scenario.CheckingId.ToString(),
                    false)
            ]);
        var tagged = await AggregateAsync(
            context,
            AggregationDimension.Category,
            selections: [new TransactionAggregationSelection(
                AggregationDimension.Tag,
                Scenario.LiveTagId.ToString(),
                false)]);

        Assert.Equal(3, minimum.Sum(item => item.RecordCount));
        Assert.Equal(2, maximum.Sum(item => item.RecordCount));
        Assert.Equal(1, liveTag.Sum(item => item.RecordCount));
        Assert.Empty(deletedTag);
        Assert.Equal(97m, selected.Sum(item => item.Amount));
        Assert.Equal(2, selected.Sum(item => item.RecordCount));
        Assert.Equal(-10.50m, Assert.Single(tagged).Amount);
    }

    [FunctionalFact]
    public async Task GivenTransactionsTable_WhenFilteredSortedAndPaged_ThenTypedRowsAndTotalsReturn()
    {
        await using var context = CreateContext();

        var report = await ReadTableAsync(
            context,
            "transactions",
            ["id", "amount", "occurredOn", "direction", "isReconciled", "createdAt",
                "attachmentNames", "description", "currencyCode"],
            [new TableFilterCriteria("amount", "gt", "5")],
            [new TableSortCriteria("amount", true)]);
        var second = await ReadTableAsync(
            context,
            "transactions",
            ["amount"],
            [new TableFilterCriteria("amount", "gt", "5")],
            [new TableSortCriteria("amount", true)],
            pageNumber: 2,
            pageSize: 2);

        Assert.Equal(5, report.TotalCount);
        var rows = report.Rows.ToArray();
        var salary = rows[0];
        Assert.Equal(100m, Assert.IsType<decimal>(salary["amount"]));
        Assert.Equal("Earning", salary["direction"]);
        Assert.False(Assert.IsType<bool>(salary["isReconciled"]));
        Assert.Equal(new DateOnly(2026, 9, 10), Assert.IsType<DateOnly>(salary["occurredOn"]));
        Assert.Equal(ReportingScenario.Now.UtcDateTime,
            Assert.IsType<DateTime>(salary["createdAt"]));
        Assert.Equal("BRL", salary["currencyCode"]);
        Assert.Equal(string.Empty, salary["attachmentNames"]);
        var groceries = rows.Single(row =>
            Assert.IsType<Guid>(row["id"]) == Scenario.GroceriesTransactionId);
        Assert.Equal("a.pdf, b.pdf", groceries["attachmentNames"]);
        Assert.Equal(
            [100m, 30m, 30m, 10.50m, 5.25m],
            rows.Select(row => (decimal)row["amount"]!));
        Assert.Equal([30m, 10.50m], second.Rows.Select(row => (decimal)row["amount"]!));
        Assert.Equal(175.75m, report.TotalGroups.Sum(item => item.Value));
        Assert.All(report.TotalGroups, item =>
        {
            Assert.Equal("amount", item.Column);
            Assert.Equal("BRL", item.CurrencyCode);
            Assert.NotNull(item.FigureDate);
        });
    }

    [FunctionalTheory]
    [InlineData("description", "contains", "50%", 1)]
    [InlineData("description", "contains", "SALARY", 1)]
    [InlineData("description", "startsWith", "Card", 1)]
    [InlineData("description", "endsWith", "_sale", 1)]
    [InlineData("occurredOn", "eq", "2026-09-14", 1)]
    [InlineData("occurredOn", "gte", "2026-09-10", 3)]
    [InlineData("createdAt", "gte", "2026-09-29T12:00:00Z", 6)]
    [InlineData("createdAt", "gt", "2026-09-30T12:00:00Z", 0)]
    [InlineData("isReconciled", "eq", "false", 6)]
    [InlineData("direction", "eq", "Earning", 2)]
    [InlineData("amount", "eq", "10.5", 1)]
    [InlineData("amount", "lte", "10", 2)]
    public async Task GivenTypedFilter_WhenTableQueried_ThenMatchingRowsAreCounted(
        string field,
        string operation,
        string value,
        int expected)
    {
        await using var context = CreateContext();

        var report = await ReadTableAsync(
            context,
            "transactions",
            ["id"],
            [new TableFilterCriteria(field, operation, value)]);

        Assert.Equal(expected, report.TotalCount);
    }

    [FunctionalFact]
    public async Task GivenUuidFilterAndIntegerTotals_WhenTableQueried_ThenBothProvidersAgree()
    {
        await using var context = CreateContext();

        var byId = await ReadTableAsync(
            context,
            "transactions",
            ["id"],
            [new TableFilterCriteria("id", "eq", Scenario.GroceriesTransactionId.ToString())]);
        var attachments = await ReadTableAsync(
            context,
            "attachments",
            ["fileName", "sizeInBytes"],
            sorts: [new TableSortCriteria("fileName", false)]);

        Assert.Equal(Scenario.GroceriesTransactionId, Assert.Single(byId.Rows)["id"]);
        Assert.Equal(["a.pdf", "b.pdf"], attachments.Rows.Select(row => row["fileName"]));
        Assert.Equal(20L, Convert.ToInt64(attachments.Rows.First()["sizeInBytes"]));
        var total = Assert.Single(attachments.TotalGroups);
        Assert.Null(total.CurrencyCode);
        Assert.Null(total.FigureDate);
        Assert.Equal(30m, total.Value);
    }

    private IEnumerable<TransactionAggregationFigureSnapshot> Bucket(
        IEnumerable<TransactionAggregationFigureSnapshot> figures,
        Guid id) => figures.Where(item => item.DimensionValue == id.ToString());

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
