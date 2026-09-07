using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class QueryRecordsAsTableQueryHandlerTests
{
    private static readonly DateOnly FigureDate = new(2026, 9, 7);

    [UnitFact]
    public async Task GivenValidCriteria_WhenHandled_ThenClampedRequestAndTypedRowsAreReturned()
    {
        var report = Report([
            new TableTotalGroupSnapshot("amount", "BRL", FigureDate, 10m),
            new TableTotalGroupSnapshot("amount", "USD", FigureDate, 2m)
        ]);
        var reader = new StubTableReader(new TableReportReadResult(
            TableReportReadOutcome.Succeeded,
            report));
        var handler = Handler(reader: reader, rate: new ExchangeRateSnapshot(
            "USD", "BRL", 5m, FigureDate, ExchangeRateSource.Manual), maximumPageSize: 25);
        var query = Query();
        query.DisplayCurrencyCode = "brl";
        query.PageSize = 999;

        var result = await handler.HandleAsync(query);

        Assert.True(result.Success);
        Assert.Equal(25, reader.Criteria!.PageSize);
        Assert.Equal(Profile().Id, reader.Criteria.UserId);
        Assert.Equal("transactions", result.Data!.RecordSet);
        Assert.Equal(TableColumnType.Decimal, Assert.Single(result.Data.Columns).Type);
        var total = Assert.Single(result.Data.Totals);
        Assert.Equal("BRL", total.CurrencyCode);
        Assert.Equal(20m, total.Value);
        Assert.Equal(5m, total.Conversions.Single(conversion =>
            conversion.SourceCurrencyCode == "USD").AppliedRate);
        Assert.Contains(TableReportMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenNoDisplayCurrency_WhenHandled_ThenTotalsRemainSplitByCurrency()
    {
        var handler = Handler(reader: new StubTableReader(new TableReportReadResult(
            TableReportReadOutcome.Succeeded,
            Report([
                new TableTotalGroupSnapshot("amount", "BRL", FigureDate, 1m),
                new TableTotalGroupSnapshot("amount", "BRL", FigureDate.AddDays(1), 2m),
                new TableTotalGroupSnapshot("amount", "USD", FigureDate, 4m)
            ]))));

        var result = await handler.HandleAsync(Query());

        Assert.Equal(2, result.Data!.Totals.Count);
        Assert.Equal(3m, result.Data.Totals.Single(total =>
            total.CurrencyCode == "BRL").Value);
        Assert.Equal(4m, result.Data.Totals.Single(total =>
            total.CurrencyCode == "USD").Value);
    }

    [UnitFact]
    public async Task GivenUnavailableRate_WhenConverted_ThenTotalIsReportedAsPartial()
    {
        var query = Query();
        query.DisplayCurrencyCode = "BRL";
        var handler = Handler(reader: new StubTableReader(new TableReportReadResult(
            TableReportReadOutcome.Succeeded,
            Report([new TableTotalGroupSnapshot("amount", "USD", FigureDate, 2m)]))));

        var result = await handler.HandleAsync(query);

        var total = Assert.Single(result.Data!.Totals);
        Assert.False(total.IsFullyConverted);
        Assert.Null(total.Value);
        Assert.Equal(FigureConversionMessages.RateUnavailable,
            Assert.Single(total.Conversions).UnconvertedReason);
    }

    [UnitTheory]
    [InlineData(TableReportReadOutcome.RecordSetUnknown, "bad", "Record set")]
    [InlineData(TableReportReadOutcome.ColumnUnknown, "bad", "Column")]
    [InlineData(TableReportReadOutcome.FilterFieldUnknown, "bad", "Filter field")]
    [InlineData(TableReportReadOutcome.FilterOperatorUnknown, "amount", "Filter operator")]
    [InlineData(TableReportReadOutcome.FilterValueInvalid, "amount", "Filter value")]
    [InlineData(TableReportReadOutcome.SortFieldUnknown, "bad", "Sort field")]
    public async Task GivenReaderRejection_WhenHandled_ThenSpecificFailureIsReturned(
        TableReportReadOutcome outcome,
        string invalidName,
        string expected)
    {
        var handler = Handler(reader: new StubTableReader(new TableReportReadResult(
            outcome,
            InvalidName: invalidName,
            InvalidOperator: "contains",
            InvalidValue: "value",
            SupportedValues: ["transactions"])));

        var result = await handler.HandleAsync(Query());

        Assert.False(result.Success);
        Assert.Contains(expected, Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [UnitFact]
    public async Task GivenInvalidInputOrMissingProfile_WhenHandled_ThenReaderIsNotCalled()
    {
        var invalidReader = new StubTableReader(new TableReportReadResult(
            TableReportReadOutcome.Succeeded,
            Report([])));
        var invalid = Query();
        invalid.PageNumber = 0;

        var invalidResult = await Handler(reader: invalidReader).HandleAsync(invalid);
        var missingReader = new StubTableReader(new TableReportReadResult(
            TableReportReadOutcome.Succeeded,
            Report([])));
        var missingResult = await Handler(
            reader: missingReader,
            missingProfile: true).HandleAsync(Query());

        Assert.Contains(TableReportMessages.InvalidPageNumber, invalidResult.Errors);
        Assert.Contains(TableReportMessages.ProfileNotFound, missingResult.Errors);
        Assert.Null(invalidReader.Criteria);
        Assert.Null(missingReader.Criteria);
    }

    [UnitFact]
    public async Task GivenUnsupportedDisplayCurrency_WhenHandled_ThenReaderIsNotCalled()
    {
        var reader = new StubTableReader(new TableReportReadResult(
            TableReportReadOutcome.Succeeded,
            Report([])));
        var query = Query();
        query.DisplayCurrencyCode = "ZZZ";

        var result = await Handler(reader: reader).HandleAsync(query);

        Assert.Contains(TableReportMessages.DisplayCurrencyUnsupported, result.Errors);
        Assert.Contains(TableReportMessages.UnknownCurrency("ZZZ"), result.Messages);
        Assert.Null(reader.Criteria);
    }

    [UnitFact]
    public async Task GivenLocalActor_WhenHandled_ThenProfileIsResolvedByPublicId()
    {
        var profiles = new StubProfileReader(Profile());
        var reader = new StubTableReader(new TableReportReadResult(
            TableReportReadOutcome.Succeeded,
            Report([])));
        var actor = new RequestActor(Profile().Id, 3, null, []) { IsLocal = true };

        var result = await Handler(reader, profiles: profiles, actor: actor).HandleAsync(Query());

        Assert.True(result.Success);
        Assert.True(profiles.PublicIdLookupUsed);
    }

    private static QueryRecordsAsTableQueryHandler Handler(
        StubTableReader? reader = null,
        bool missingProfile = false,
        StubProfileReader? profiles = null,
        RequestActor? actor = null,
        ExchangeRateSnapshot? rate = null,
        int maximumPageSize = 100)
    {
        var resolvedProfile = missingProfile ? null : Profile();
        return new QueryRecordsAsTableQueryHandler(
            new QueryRecordsAsTableQueryValidator(),
            profiles ?? new StubProfileReader(resolvedProfile),
            reader ?? new StubTableReader(new TableReportReadResult(
                TableReportReadOutcome.Succeeded,
                Report([]))),
            new StubCurrencyReader(),
            new StubRateReader(rate),
            new StubActor(actor ?? new RequestActor(
                resolvedProfile?.ExternalSubject ?? Guid.NewGuid(), 3, null, [])),
            new PaginationOptions(maximumPageSize));
    }

    private static QueryRecordsAsTableQuery Query() => new()
    {
        RecordSet = "transactions",
        Columns = ["amount"],
        Filters = [new() { Field = "amount", Operator = "gte", Value = "1" }],
        Sorts = [new() { Field = "amount", Descending = true }],
        PageNumber = 1,
        PageSize = 10
    };

    private static TableReportSnapshot Report(
        IReadOnlyCollection<TableTotalGroupSnapshot> totals) => new(
        "transactions",
        [new TableColumnSnapshot("amount", TableColumnType.Decimal, true, "currencyCode")],
        [new Dictionary<string, object?> { ["amount"] = 10m }],
        1,
        1,
        10,
        totals);

    private static UserProfileSnapshot Profile() => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000002"),
        "Owner",
        "BRL",
        false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private sealed class StubTableReader(TableReportReadResult result) : ITableReportReader
    {
        public TableReportCriteria? Criteria { get; private set; }

        public Task<TableReportReadResult> ReadAsync(
            TableReportCriteria criteria,
            CancellationToken cancellationToken)
        {
            Criteria = criteria;
            return Task.FromResult(result);
        }
    }

    private sealed class StubCurrencyReader : ICurrencyReader
    {
        public Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<
                CurrencySnapshot>>([]);

        public Task<CurrencySnapshot?> FindByCodeAsync(
            string code,
            CancellationToken cancellationToken) => Task.FromResult<CurrencySnapshot?>(
            code is "BRL" or "USD" ? new CurrencySnapshot(code, code, 2) : null);
    }

    private sealed class StubRateReader(ExchangeRateSnapshot? rate) : IExchangeRateReader
    {
        public Task<ExchangeRateSnapshot?> FindApplicableAsync(
            string baseCurrencyCode,
            string quoteCurrencyCode,
            DateOnly figureDate,
            CancellationToken cancellationToken) => Task.FromResult(rate);
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public bool PublicIdLookupUsed { get; private set; }

        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject,
            CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId,
            CancellationToken cancellationToken)
        {
            PublicIdLookupUsed = true;
            return Task.FromResult(profile);
        }
    }

    private sealed class StubActor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }
}
