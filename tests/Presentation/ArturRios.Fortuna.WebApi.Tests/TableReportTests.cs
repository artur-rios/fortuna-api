using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class TableReportTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenColumnsFiltersSortAndPage_WhenQueried_ThenOwnedLiveTypedRowsAndTotalsReturn()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(owner, "Owned account", "BRL");
        var category = await SeedCategoryAsync(subject, "General");
        await RecordAsync(owner, account, category, 10m, Today.AddDays(-1), "Owned older");
        var newest = await RecordAsync(owner, account, category, 20m, Today, "Owned newest");
        var deleted = await RecordAsync(owner, account, category, 90m, Today, "Owned deleted");
        await SoftDeleteAsync(deleted);
        using var other = factory.CreateClient();
        var otherSubject = Guid.NewGuid();
        Authorize(other, otherSubject, HeimdallRoles.User);
        var foreignAccount = await CreateAccountAsync(other, "Foreign", "BRL");
        var foreignCategory = await SeedCategoryAsync(otherSubject, "Foreign");
        await RecordAsync(other, foreignAccount, foreignCategory, 100m, Today, "Owned foreign");

        var response = await owner.PostAsJsonAsync("/api/reports/table", new QueryRecordsAsTableQuery
        {
            RecordSet = "transactions",
            Columns = ["id", "description", "amount", "currencyCode", "occurredOn"],
            Filters =
            [
                new TableFilterInput
                {
                    Field = "description",
                    Operator = "contains",
                    Value = "Owned"
                }
            ],
            Sorts = [new TableSortInput { Field = "amount", Descending = true }],
            PageNumber = 1,
            PageSize = 1
        });
        var report = await response.Content.ReadFromJsonAsync<TableEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("transactions", report!.Data!.RecordSet);
        Assert.Equal(2, report.Data.TotalCount);
        Assert.Equal(1, report.Data.PageSize);
        var row = Assert.Single(report.Data.Rows);
        Assert.Equal(newest, row["id"].GetGuid());
        Assert.Equal(20m, row["amount"].GetDecimal());
        Assert.Equal(TableColumnType.Decimal,
            report.Data.Columns.Single(column => column.Name == "amount").Type);
        var total = Assert.Single(report.Data.Totals);
        Assert.Equal("BRL", total.CurrencyCode);
        Assert.Equal(30m, total.Value);
    }

    [FunctionalFact]
    public async Task GivenUnknownNamesOperatorOrValue_WhenQueried_ThenBadRequestNamesCause()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var cases = new (QueryRecordsAsTableQuery Query, string Expected)[]
        {
            (Query("unknown", ["id"]), "unknown"),
            (Query("transactions", ["mystery"]), "mystery"),
            (Query("transactions", ["id"], filters:
                [new() { Field = "mystery", Value = "x" }]), "mystery"),
            (Query("transactions", ["id"], filters:
                [new() { Field = "amount", Operator = "contains", Value = "1" }]), "contains"),
            (Query("transactions", ["id"], filters:
                [new() { Field = "amount", Value = "not-a-number" }]), "not-a-number"),
            (Query("transactions", ["id"], filters:
                [new() { Field = "id", Value = "not-a-uuid" }]), "not-a-uuid"),
            (Query("transactions", ["id"], filters:
                [new() { Field = "occurredOn", Value = "not-a-date" }]), "not-a-date"),
            (Query("transactions", ["id"], filters:
                [new() { Field = "createdAt", Value = "not-a-time" }]), "not-a-time"),
            (Query("transactions", ["id"], filters:
                [new() { Field = "isReconciled", Value = "not-a-bool" }]), "not-a-bool"),
            (Query("transactions", ["id"], filters:
                [new() { Field = "direction", Value = "Sideways" }]), "Sideways"),
            (Query("transactions", ["id"], sorts:
                [new() { Field = "mystery" }]), "mystery")
        };

        foreach (var item in cases)
        {
            var response = await client.PostAsJsonAsync("/api/reports/table", item.Query);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(item.Expected, await response.Content.ReadAsStringAsync(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [FunctionalFact]
    public async Task GivenOversizedPage_WhenQueried_ThenAppliedMaximumIsReported()
    {
        await using var factory = CreateFactory(maximumPageSize: 1);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        await CreateAccountAsync(client, "First", "BRL");
        await CreateAccountAsync(client, "Second", "BRL");

        var response = await client.PostAsJsonAsync("/api/reports/table", new QueryRecordsAsTableQuery
        {
            RecordSet = "accounts",
            Columns = ["id", "name"],
            PageNumber = 1,
            PageSize = 999
        });
        var report = await response.Content.ReadFromJsonAsync<TableEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, report!.Data!.PageSize);
        Assert.Single(report.Data.Rows);
        Assert.Equal(2, report.Data.TotalCount);
    }

    [FunctionalFact]
    public async Task GivenTypedFilters_WhenQueried_ThenEverySupportedValueKindIsAppliedSafely()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Typed account", "BRL");
        var category = await SeedCategoryAsync(subject, "Typed category");
        var transaction = await RecordAsync(
            client,
            account,
            category,
            12.5m,
            Today,
            "Typed value");
        await CreateCardAsync(client, "Typed card", closingDay: 20);
        var filters = new TableFilterInput[]
        {
            new() { Field = "id", Operator = "eq", Value = transaction.ToString("D") },
            new() { Field = "description", Operator = "eq", Value = "Typed value" },
            new() { Field = "description", Operator = "ne", Value = "Other" },
            new() { Field = "description", Operator = "startsWith", Value = "Typed" },
            new() { Field = "description", Operator = "endsWith", Value = "value" },
            new() { Field = "direction", Operator = "eq", Value = "Expense" },
            new() { Field = "amount", Operator = "gt", Value = "12" },
            new() { Field = "amount", Operator = "gte", Value = "12.5" },
            new() { Field = "amount", Operator = "lt", Value = "13" },
            new() { Field = "amount", Operator = "lte", Value = "12.5" },
            new() { Field = "occurredOn", Operator = "eq", Value = $"{Today:yyyy-MM-dd}" },
            new() { Field = "isReconciled", Operator = "eq", Value = "false" },
            new()
            {
                Field = "createdAt",
                Operator = "lt",
                Value = DateTimeOffset.UtcNow.AddDays(1).ToString("O")
            }
        };

        foreach (var filter in filters)
        {
            var response = await client.PostAsJsonAsync(
                "/api/reports/table",
                Query("transactions", ["id"], filters: [filter]));
            var report = await response.Content.ReadFromJsonAsync<TableEnvelope>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(transaction, Assert.Single(report!.Data!.Rows)["id"].GetGuid());
        }

        var integerResponse = await client.PostAsJsonAsync(
            "/api/reports/table",
            Query("creditCards", ["closingDay"], filters:
                [new() { Field = "closingDay", Operator = "gte", Value = "20" }]));
        var integerReport = await integerResponse.Content.ReadFromJsonAsync<TableEnvelope>();

        Assert.Equal(HttpStatusCode.OK, integerResponse.StatusCode);
        var total = Assert.Single(integerReport!.Data!.Totals);
        Assert.Null(total.CurrencyCode);
        Assert.Equal(20m, total.Value);
        Assert.True(Assert.Single(integerReport.Data.Columns).IsNumeric);
    }

    [FunctionalFact]
    public async Task GivenSeveralCurrencies_WhenQueried_ThenTotalsSplitOrConvertWithRates()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var brl = await CreateAccountAsync(client, "Brazil", "BRL");
        var usd = await CreateAccountAsync(client, "Dollar", "USD");
        var category = await SeedCategoryAsync(subject, "General");
        await RecordAsync(client, brl, category, 10m, Today, "BRL");
        await RecordAsync(client, usd, category, 2m, Today, "USD");
        await SeedRateAsync("USD", "BRL", 5m, Today);
        var rawQuery = Query("transactions", ["amount", "currencyCode"]);
        var convertedQuery = Query("transactions", ["amount", "currencyCode"]);
        convertedQuery.DisplayCurrencyCode = "BRL";

        var rawResponse = await client.PostAsJsonAsync("/api/reports/table", rawQuery);
        var raw = await rawResponse.Content.ReadFromJsonAsync<TableEnvelope>();
        var convertedResponse = await client.PostAsJsonAsync("/api/reports/table", convertedQuery);
        var converted = await convertedResponse.Content.ReadFromJsonAsync<TableEnvelope>();

        Assert.Equal(HttpStatusCode.OK, rawResponse.StatusCode);
        Assert.Equal(2, raw!.Data!.Totals.Count);
        Assert.Equal(10m, raw.Data.Totals.Single(total => total.CurrencyCode == "BRL").Value);
        Assert.Equal(2m, raw.Data.Totals.Single(total => total.CurrencyCode == "USD").Value);
        var total = Assert.Single(converted!.Data!.Totals);
        Assert.Equal("BRL", total.CurrencyCode);
        Assert.Equal(20m, total.Value);
        Assert.True(total.IsFullyConverted);
        Assert.Equal(5m, total.Conversions.Single(item =>
            item.SourceCurrencyCode == "USD").AppliedRate);
    }

    [FunctionalFact]
    public async Task GivenEmptySets_WhenQueried_ThenEverySupportedGridStillReturnsColumns()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        await CreateAccountAsync(client, "Provision profile", "BRL");
        var sets = new (string Name, string[] Columns)[]
        {
            ("transactions", ["id"]),
            ("accounts", ["id"]),
            ("creditCards", ["id"]),
            ("investments", ["id"]),
            ("categories", ["id"]),
            ("tags", ["id"]),
            ("counterparties", ["id"]),
            ("budgets", ["id"]),
            ("goals", ["id"]),
            ("creditCardStatements", ["id"]),
            ("investmentMovements", ["id"]),
            ("investmentValuations", ["id"]),
            ("recurringTransactions", ["id"]),
            ("installmentPlans", ["id"]),
            ("transfers", ["id"]),
            ("attachments", ["id"]),
            ("connections", ["id"]),
            ("importJobs", ["id"])
        };

        foreach (var set in sets)
        {
            var response = await client.PostAsJsonAsync(
                "/api/reports/table",
                Query(set.Name, set.Columns));
            var report = await response.Content.ReadFromJsonAsync<TableEnvelope>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(report!.Data!.Columns);
            if (set.Name != "accounts")
            {
                Assert.Empty(report.Data.Rows);
                Assert.Equal(0, report.Data.TotalCount);
            }
        }
    }

    [FunctionalFact]
    public async Task GivenAnonymousOrAdministrator_WhenTableQueried_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var query = Query("transactions", ["id"]);

        var anonymousResponse = await anonymous.PostAsJsonAsync("/api/reports/table", query);
        var administratorResponse = await administrator.PostAsJsonAsync("/api/reports/table", query);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private static QueryRecordsAsTableQuery Query(
        string recordSet,
        IReadOnlyCollection<string> columns,
        IReadOnlyCollection<TableFilterInput>? filters = null,
        IReadOnlyCollection<TableSortInput>? sorts = null) => new()
        {
            RecordSet = recordSet,
            Columns = columns,
            Filters = filters ?? [],
            Sorts = sorts ?? [],
            PageNumber = 1,
            PageSize = 100
        };

    private async Task<Guid> SeedCategoryAsync(Guid subject, string name)
    {
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var category = new Category(user, name, DateTimeOffset.UtcNow);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category.PublicId;
    }

    private async Task SeedRateAsync(
        string baseCode,
        string quoteCode,
        decimal rate,
        DateOnly rateDate)
    {
        await using var context = CreateContext();
        var baseCurrency = await context.Currencies.SingleAsync(item => item.Code == baseCode);
        var quoteCurrency = await context.Currencies.SingleAsync(item => item.Code == quoteCode);
        context.ExchangeRates.Add(new ExchangeRate(
            baseCurrency.Id,
            quoteCurrency.Id,
            rate,
            rateDate,
            ExchangeRateSource.Manual));
        await context.SaveChangesAsync();
    }

    private async Task SoftDeleteAsync(Guid id)
    {
        await using var context = CreateContext();
        var transaction = await context.FinancialTransactions.SingleAsync(item =>
            item.PublicId == id);
        transaction.SoftDelete(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();
    }

    private static async Task<Guid> CreateAccountAsync(
        HttpClient client,
        string name,
        string currencyCode)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = name,
            AccountType = FinancialAccountType.Checking,
            CurrencyCode = currencyCode,
            OpeningBalance = 0m
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> RecordAsync(
        HttpClient client,
        Guid accountId,
        Guid categoryId,
        decimal amount,
        DateOnly occurredOn,
        string description)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = occurredOn,
            Amount = amount,
            Direction = TransactionDirection.Expense,
            FinancialAccountId = accountId,
            CategoryId = categoryId,
            Description = description
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> CreateCardAsync(
        HttpClient client,
        string name,
        short closingDay)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = "Example Bank",
            CurrencyCode = "BRL",
            CreditLimit = 1000m,
            ClosingDay = closingDay,
            DueDay = 5,
            LastFourDigits = "1234"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private WebApplicationFactory<Program> CreateFactory(int maximumPageSize = 100)
    {
        foreach (var setting in ValidSettings())
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<PaginationOptions>();
                services.AddSingleton(new PaginationOptions(maximumPageSize));
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString()));
            });
        });
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .Options;
        return new AppDbContext(
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Table Owner"
        };
        var configuration = new JwtConfiguration(
            3600,
            Issuer,
            Audience,
            Secret,
            new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            new JwtHandler().CreateToken(configuration));
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] =
            "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record IdEnvelope(IdData? Data);
    private sealed record IdData(Guid Id);
    private sealed record TableEnvelope(TableData? Data);
    private sealed record TableData(
        string RecordSet,
        IReadOnlyCollection<TableColumnData> Columns,
        IReadOnlyCollection<IReadOnlyDictionary<string, JsonElement>> Rows,
        int TotalCount,
        int PageNumber,
        int PageSize,
        IReadOnlyCollection<TableTotalData> Totals);
    private sealed record TableColumnData(
        string Name,
        TableColumnType Type,
        bool IsNumeric,
        string? CurrencyColumn);
    private sealed record TableTotalData(
        string Column,
        string? CurrencyCode,
        decimal? Value,
        bool IsFullyConverted,
        IReadOnlyCollection<TableConversionData> Conversions);
    private sealed record TableConversionData(
        string? SourceCurrencyCode,
        decimal SourceValue,
        DateOnly? FigureDate,
        decimal? ConvertedValue,
        decimal? AppliedRate,
        DateOnly? RateDate,
        ExchangeRateSource? RateSource,
        string? UnconvertedReason);
}
