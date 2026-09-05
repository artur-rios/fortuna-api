using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
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

public sealed class TransactionSearchTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenOwnedForeignOrMissingTransaction_WhenRead_ThenIsolationAndDetailsAreReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(owner, "Detail", "BRL");
        var categoryId = await SeedCategoryAsync(subject, "Dining");
        var recorded = await RecordAsync(
            owner,
            categoryId,
            25m,
            TransactionDirection.Expense,
            Today,
            "Team lunch",
            "Corner Cafe",
            ["Food"],
            financialAccountId: account.Id);
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);

        var ownedResponse = await owner.GetAsync($"/api/transactions/{recorded.Id}");
        var owned = await ownedResponse.Content.ReadFromJsonAsync<TransactionEnvelope>();
        var foreign = await other.GetAsync($"/api/transactions/{recorded.Id}");
        var missing = await other.GetAsync($"/api/transactions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, ownedResponse.StatusCode);
        Assert.Equal(recorded.Id, owned?.Data?.Id);
        Assert.Equal(account.Id, owned?.Data?.FinancialAccountId);
        Assert.Equal("Detail", owned?.Data?.FinancialAccountName);
        Assert.Equal("Dining", owned?.Data?.CategoryName);
        Assert.Equal("Corner Cafe", owned?.Data?.CounterpartyName);
        Assert.Equal("Food", owned?.Data?.Tags.Single().Name);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains(TransactionMessages.NotFound,
            await foreign.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(TransactionMessages.NotFound,
            await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenCombinedFiltersAndCardFilter_WhenSearched_ThenOnlyMatchesAreReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Checking", "BRL");
        var card = await CreateCardAsync(client, "Rewards");
        var categoryId = await SeedCategoryAsync(subject, "Dining");
        var matching = await RecordAsync(
            client,
            categoryId,
            25m,
            TransactionDirection.Expense,
            Today.AddDays(-1),
            "Team lunch",
            "Corner Cafe",
            ["Food"],
            financialAccountId: account.Id);
        await RecordAsync(
            client,
            categoryId,
            10m,
            TransactionDirection.Earning,
            Today,
            "Refund",
            financialAccountId: account.Id);
        var cardTransaction = await RecordAsync(
            client,
            categoryId,
            30m,
            TransactionDirection.Expense,
            Today,
            "Card dinner",
            creditCardId: card.Id);

        var response = await client.GetAsync(
            $"/api/transactions?From={Today.AddDays(-2):yyyy-MM-dd}" +
            $"&To={Today:yyyy-MM-dd}&FinancialAccountId={account.Id}" +
            $"&CategoryId={matching.CategoryId}&TagId={matching.Tags.Single().Id}" +
            $"&CounterpartyId={matching.CounterpartyId}&Direction=1" +
            "&MinimumAmount=20&MaximumAmount=30&Text=lunch" +
            "&SortBy=Amount&Descending=true");
        var page = await response.Content.ReadFromJsonAsync<SearchEnvelope>();
        var cardPage = await client.GetFromJsonAsync<SearchEnvelope>(
            $"/api/transactions?CreditCardId={card.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(matching.Id, Assert.Single(page!.Data!.Items).Id);
        Assert.Equal(1, page.Data.TotalItems);
        Assert.Equal(cardTransaction.Id, Assert.Single(cardPage!.Data!.Items).Id);
    }

    [FunctionalFact]
    public async Task GivenNoCriteriaAndSeveralOwners_WhenSearched_ThenRecentOwnedPageIsReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Owned", "BRL");
        var categoryId = await SeedCategoryAsync(subject, "General");
        var older = await RecordAsync(
            client,
            categoryId,
            10m,
            TransactionDirection.Expense,
            Today.AddDays(-2),
            "Older",
            financialAccountId: account.Id);
        var newer = await RecordAsync(
            client,
            categoryId,
            20m,
            TransactionDirection.Expense,
            Today.AddDays(-1),
            "Newer",
            financialAccountId: account.Id);
        using var foreign = factory.CreateClient();
        var foreignSubject = Guid.NewGuid();
        Authorize(foreign, foreignSubject, HeimdallRoles.User);
        var foreignAccount = await CreateAccountAsync(foreign, "Foreign", "BRL");
        var foreignCategory = await SeedCategoryAsync(foreignSubject, "Foreign");
        await RecordAsync(
            foreign,
            foreignCategory,
            99m,
            TransactionDirection.Expense,
            Today,
            "Private",
            financialAccountId: foreignAccount.Id);

        var first = await client.GetFromJsonAsync<SearchEnvelope>(
            "/api/transactions?PageNumber=1&PageSize=1");
        var second = await client.GetFromJsonAsync<SearchEnvelope>(
            "/api/transactions?PageNumber=2&PageSize=1");

        Assert.Equal(newer.Id, Assert.Single(first!.Data!.Items).Id);
        Assert.Equal(older.Id, Assert.Single(second!.Data!.Items).Id);
        Assert.Equal(2, first.Data.TotalItems);
        Assert.Equal(2, first.Data.TotalPages);
    }

    [FunctionalFact]
    public async Task GivenUnsupportedSortFilterOrCurrency_WhenSearched_ThenBadRequestNamesCause()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var sort = await client.GetAsync("/api/transactions?SortBy=Target");
        var filter = await client.GetAsync("/api/transactions?Institution=Bank");
        var currency = await client.GetAsync(
            "/api/transactions?DisplayCurrencyCode=ZZZ");

        Assert.Equal(HttpStatusCode.BadRequest, sort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, filter.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, currency.StatusCode);
        Assert.Contains("SortBy", await sort.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Contains("Institution", await filter.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Contains("ZZZ", await currency.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenSeveralCurrencies_WhenSearched_ThenRawAndConvertedTotalsAreAttributable()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var brl = await CreateAccountAsync(client, "Brazil", "BRL");
        var usd = await CreateAccountAsync(client, "Dollar", "USD");
        var categoryId = await SeedCategoryAsync(subject, "General");
        await RecordAsync(
            client,
            categoryId,
            10m,
            TransactionDirection.Expense,
            Today,
            "BRL expense",
            financialAccountId: brl.Id);
        await RecordAsync(
            client,
            categoryId,
            4m,
            TransactionDirection.Earning,
            Today,
            "BRL earning",
            financialAccountId: brl.Id);
        await RecordAsync(
            client,
            categoryId,
            2m,
            TransactionDirection.Expense,
            Today,
            "USD expense",
            financialAccountId: usd.Id);
        await SeedRateAsync("USD", "BRL", 5m, Today);

        var raw = await client.GetFromJsonAsync<SearchEnvelope>("/api/transactions");
        var converted = await client.GetFromJsonAsync<SearchEnvelope>(
            $"/api/transactions?DisplayCurrencyCode=BRL&FigureDate={Today:yyyy-MM-dd}");

        Assert.Equal(2, raw!.Data!.Totals.ByCurrency.Count);
        Assert.Null(raw.Data.Totals.DisplayNet);
        Assert.Equal(-6m, raw.Data.Totals.ByCurrency.Single(item =>
            item.CurrencyCode == "BRL").Net);
        Assert.Equal(-2m, raw.Data.Totals.ByCurrency.Single(item =>
            item.CurrencyCode == "USD").Net);
        Assert.Equal(20m, converted!.Data!.Totals.DisplayExpense);
        Assert.Equal(4m, converted.Data.Totals.DisplayEarning);
        Assert.Equal(-16m, converted.Data.Totals.DisplayNet);
        var usdTotal = converted.Data.Totals.ByCurrency.Single(item =>
            item.CurrencyCode == "USD");
        Assert.Equal(5m, usdTotal.AppliedRate);
        Assert.Equal(Today, usdTotal.RateDate);
        Assert.Equal(ExchangeRateSource.Manual, usdTotal.RateSource);
    }

    [FunctionalFact]
    public async Task GivenDeletedTransaction_WhenExplicitlyRequested_ThenItIsMarkedAndExcludedFromTotals()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Lifecycle", "BRL");
        var categoryId = await SeedCategoryAsync(subject, "General");
        var deleted = await RecordAsync(
            client,
            categoryId,
            100m,
            TransactionDirection.Expense,
            Today,
            "Deleted",
            financialAccountId: account.Id);
        await RecordAsync(
            client,
            categoryId,
            20m,
            TransactionDirection.Earning,
            Today,
            "Live",
            financialAccountId: account.Id);
        await SoftDeleteAsync(deleted.Id);

        var hidden = await client.GetFromJsonAsync<SearchEnvelope>("/api/transactions");
        var visible = await client.GetFromJsonAsync<SearchEnvelope>(
            "/api/transactions?IncludeDeleted=true");
        var hiddenDetail = await client.GetAsync($"/api/transactions/{deleted.Id}");
        var visibleDetail = await client.GetFromJsonAsync<TransactionEnvelope>(
            $"/api/transactions/{deleted.Id}?includeDeleted=true");

        Assert.Single(hidden!.Data!.Items);
        Assert.Equal(2, visible!.Data!.Items.Count);
        Assert.True(visible.Data.Items.Single(item => item.Id == deleted.Id).IsDeleted);
        Assert.Equal(0m, visible.Data.Totals.ByCurrency.Single().Expense);
        Assert.Equal(20m, visible.Data.Totals.ByCurrency.Single().Earning);
        Assert.Equal(HttpStatusCode.NotFound, hiddenDetail.StatusCode);
        Assert.True(visibleDetail!.Data!.IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenTransferRows_WhenSearched_ThenRowsAppearButTotalsExcludeThem()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var origin = await CreateAccountAsync(client, "Origin", "BRL");
        var destination = await CreateAccountAsync(client, "Destination", "BRL");
        var categoryId = await SeedCategoryAsync(subject, "Transfers");
        var outbound = await RecordAsync(
            client,
            categoryId,
            50m,
            TransactionDirection.Expense,
            Today,
            "Transfer out",
            financialAccountId: origin.Id);
        var inbound = await RecordAsync(
            client,
            categoryId,
            50m,
            TransactionDirection.Earning,
            Today,
            "Transfer in",
            financialAccountId: destination.Id);
        await LinkTransferAsync(outbound.Id, inbound.Id);

        var result = await client.GetFromJsonAsync<SearchEnvelope>("/api/transactions");

        Assert.Equal(2, result!.Data!.Items.Count);
        Assert.All(result.Data.Items, item => Assert.True(item.IsTransfer));
        Assert.Empty(result.Data.Totals.ByCurrency);
    }

    [FunctionalFact]
    public async Task GivenAnonymousOrAdministrator_WhenTransactionsAreViewed_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var anonymousSearch = await anonymous.GetAsync("/api/transactions");
        var administratorSearch = await administrator.GetAsync("/api/transactions");
        var anonymousDetail = await anonymous.GetAsync($"/api/transactions/{Guid.NewGuid()}");
        var administratorDetail = await administrator.GetAsync(
            $"/api/transactions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousSearch.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorSearch.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDetail.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorDetail.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

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

    private async Task LinkTransferAsync(Guid outboundId, Guid inboundId)
    {
        await using var context = CreateContext();
        var outbound = await context.FinancialTransactions
            .Include(item => item.User)
            .SingleAsync(item => item.PublicId == outboundId);
        var inbound = await context.FinancialTransactions
            .Include(item => item.User)
            .SingleAsync(item => item.PublicId == inboundId);
        context.Transfers.Add(new Transfer(
            outbound,
            inbound,
            appliedRate: null,
            rateDate: null,
            DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    private static async Task<AccountData> CreateAccountAsync(
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
        return (await response.Content.ReadFromJsonAsync<AccountEnvelope>())!.Data!;
    }

    private static async Task<CardData> CreateCardAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = "Example Bank",
            CurrencyCode = "BRL",
            CreditLimit = 1000m,
            ClosingDay = 20,
            DueDay = 5,
            LastFourDigits = "1234"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardEnvelope>())!.Data!;
    }

    private static async Task<TransactionData> RecordAsync(
        HttpClient client,
        Guid categoryId,
        decimal amount,
        TransactionDirection direction,
        DateOnly occurredOn,
        string description,
        string? counterparty = null,
        string[]? tags = null,
        Guid? financialAccountId = null,
        Guid? creditCardId = null)
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new
        {
            OccurredOn = occurredOn,
            Amount = amount,
            Direction = direction,
            FinancialAccountId = financialAccountId,
            CreditCardId = creditCardId,
            CategoryId = categoryId,
            Description = description,
            Counterparty = counterparty,
            Tags = tags
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionEnvelope>())!.Data!;
    }

    private WebApplicationFactory<Program> CreateFactory()
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
            DisplayName = "Transaction Owner"
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

    private sealed record AccountEnvelope(AccountData? Data);
    private sealed record AccountData(Guid Id);
    private sealed record CardEnvelope(CardData? Data);
    private sealed record CardData(Guid Id);
    private sealed record TransactionEnvelope(
        TransactionData? Data,
        IReadOnlyCollection<string> Messages,
        IReadOnlyCollection<string> Errors);
    private sealed record SearchEnvelope(
        SearchData? Data,
        IReadOnlyCollection<string> Messages,
        IReadOnlyCollection<string> Errors);
    private sealed record SearchData(
        IReadOnlyCollection<TransactionData> Items,
        int PageNumber,
        int PageSize,
        int TotalItems,
        int TotalPages,
        TotalsData Totals);
    private sealed record TotalsData(
        IReadOnlyCollection<CurrencyTotalData> ByCurrency,
        string? DisplayCurrencyCode,
        decimal? DisplayExpense,
        decimal? DisplayEarning,
        decimal? DisplayNet);
    private sealed record CurrencyTotalData(
        string CurrencyCode,
        decimal Expense,
        decimal Earning,
        decimal Net,
        string? DisplayCurrencyCode,
        decimal? DisplayExpense,
        decimal? DisplayEarning,
        decimal? DisplayNet,
        decimal? AppliedRate,
        DateOnly? RateDate,
        ExchangeRateSource? RateSource,
        string? UnconvertedReason);
    private sealed record TransactionData(
        Guid Id,
        Guid? FinancialAccountId,
        string? FinancialAccountName,
        Guid? CreditCardId,
        string? CreditCardName,
        Guid CategoryId,
        string CategoryName,
        Guid? CounterpartyId,
        string? CounterpartyName,
        TransactionDirection Direction,
        decimal Amount,
        string CurrencyCode,
        DateOnly OccurredOn,
        string? Description,
        TransactionSourceType SourceType,
        bool IsReconciled,
        bool IsTransfer,
        Guid? StatementId,
        bool IsLateArriving,
        IReadOnlyCollection<TransactionTagData> Tags,
        bool IsDeleted,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
    private sealed record TransactionTagData(Guid Id, string Name);
}
