using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
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

public sealed class TransactionAggregationTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateOnly Start = new(2026, 9, 1);
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenPeriodData_WhenAggregated_ThenIsolationGapsTransfersAndRatesAreCorrect()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        var brlAccount = await CreateAccountAsync(owner, "Brazil", "BRL");
        var usdAccount = await CreateAccountAsync(owner, "Dollar", "USD");
        var category = await SeedCategoryAsync(subject, "General");
        await SeedTransactionAsync(brlAccount, category.Id, TransactionDirection.Expense, 10m,
            Start, "expense");
        await SeedTransactionAsync(brlAccount, category.Id, TransactionDirection.Earning, 4m,
            Start, "earning");
        await SeedTransactionAsync(usdAccount, category.Id, TransactionDirection.Expense, 2m,
            Start.AddDays(2), "dollar");
        var deleted = await SeedTransactionAsync(brlAccount, category.Id,
            TransactionDirection.Expense, 50m, Start, "deleted");
        await SoftDeleteAsync(deleted);
        var secondAccount = await CreateAccountAsync(owner, "Transfer target", "BRL");
        await SeedTransferAsync(brlAccount, secondAccount, category.Id, Start, 100m);
        await SeedRateAsync("USD", "BRL", 5m, Start.AddDays(2));

        using var other = factory.CreateClient();
        var otherSubject = Guid.NewGuid();
        Authorize(other, otherSubject, HeimdallRoles.User);
        var otherAccount = await CreateAccountAsync(other, "Other", "BRL");
        var otherCategory = await SeedCategoryAsync(otherSubject, "Other");
        await SeedTransactionAsync(otherAccount, otherCategory.Id, TransactionDirection.Expense,
            999m, Start, "foreign");

        var response = await owner.GetAsync(
            $"/api/reports/aggregate?dimension=period&granularity=day&from={Start:yyyy-MM-dd}" +
            $"&to={Start.AddDays(2):yyyy-MM-dd}&displayCurrencyCode=BRL");
        var result = await response.Content.ReadFromJsonAsync<AggregationEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("period", result!.Data!.Dimension);
        Assert.Equal("BRL", result.Data.DisplayCurrencyCode);
        Assert.True(result.Data.IsFullyConverted);
        var buckets = result.Data.Buckets.ToArray();
        Assert.Equal(3, buckets.Length);
        Assert.Equal(-6m, buckets[0].Total);
        Assert.Equal(0.375m, buckets[0].Share);
        Assert.Equal(0m, buckets[1].Total);
        Assert.Empty(buckets[1].Conversions);
        Assert.Equal(-10m, buckets[2].Total);
        Assert.Equal(0.625m, buckets[2].Share);
        Assert.Equal(5m, Assert.Single(buckets[2].Conversions).AppliedRate);
        Assert.Equal(Start.AddDays(2), buckets[2].PeriodStart);
        Assert.False(string.IsNullOrWhiteSpace(buckets[2].DrillDownKey));
    }

    [FunctionalFact]
    public async Task GivenAllDimensionsRollupAndFilters_WhenAggregated_ThenExpectedBucketsReturn()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var accountId = await CreateAccountAsync(client, "Checking", "BRL");
        var cardId = await CreateCardAsync(client, "Purple", "BRL");
        var classification = await SeedClassificationAsync(subject);
        await SeedTransactionAsync(
            accountId,
            classification.Child.Id,
            TransactionDirection.Expense,
            12m,
            Start,
            "tagged coffee",
            classification.Counterparty,
            [classification.Tag]);
        await SeedCardTransactionAsync(
            cardId,
            classification.Root.Id,
            TransactionDirection.Expense,
            8m,
            Start,
            "card purchase");

        var rolled = await AggregateAsync(client, "category", "&rollupCategories=true");
        var categories = await AggregateAsync(client, "category");
        var accounts = await AggregateAsync(client, "account");
        var cards = await AggregateAsync(client, "card");
        var counterparties = await AggregateAsync(client, "counterparty");
        var tags = await AggregateAsync(client, "tag");
        var filtered = await AggregateAsync(client, "category",
            $"&financialAccountId={accountId:D}&text=coffee&direction=Expense");
        var cardFiltered = await AggregateAsync(client, "category",
            $"&creditCardId={cardId:D}");
        var categoryFiltered = await AggregateAsync(client, "category",
            $"&categoryId={classification.Child.PublicId:D}");
        var tagFiltered = await AggregateAsync(client, "category",
            $"&tagId={classification.Tag.PublicId:D}");
        var counterpartyFiltered = await AggregateAsync(client, "category",
            $"&counterpartyId={classification.Counterparty.PublicId:D}");
        var amountFiltered = await AggregateAsync(client, "category",
            "&minimumAmount=10&maximumAmount=15");

        var root = Assert.Single(rolled.Data!.Buckets);
        Assert.Equal("Root", root.Label);
        Assert.Equal(-20m, root.Total);
        Assert.Equal(2, categories.Data!.Buckets.Count);
        Assert.Equal(-12m, Assert.Single(accounts.Data!.Buckets).Total);
        Assert.Equal("Checking", Assert.Single(accounts.Data.Buckets).Label);
        Assert.Equal(-8m, Assert.Single(cards.Data!.Buckets).Total);
        Assert.Equal("Purple", Assert.Single(cards.Data.Buckets).Label);
        Assert.Equal(2, counterparties.Data!.Buckets.Count);
        Assert.Contains(counterparties.Data.Buckets, bucket => bucket.Label == "Merchant");
        Assert.Contains(counterparties.Data.Buckets, bucket => bucket.Label == "No counterparty");
        Assert.Equal(-12m, Assert.Single(tags.Data!.Buckets).Total);
        Assert.Equal("Coffee", Assert.Single(tags.Data.Buckets).Label);
        Assert.Equal("Child", Assert.Single(filtered.Data!.Buckets).Label);
        Assert.Equal("Root", Assert.Single(cardFiltered.Data!.Buckets).Label);
        Assert.Equal("Child", Assert.Single(categoryFiltered.Data!.Buckets).Label);
        Assert.Equal("Child", Assert.Single(tagFiltered.Data!.Buckets).Label);
        Assert.Equal("Child", Assert.Single(counterpartyFiltered.Data!.Buckets).Label);
        Assert.Equal("Child", Assert.Single(amountFiltered.Data!.Buckets).Label);
    }

    [FunctionalFact]
    public async Task GivenEveryPeriodGranularity_WhenAggregated_ThenCalendarBucketReturns()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        var account = await CreateAccountAsync(client, "Calendar", "BRL");
        var category = await SeedCategoryAsync(subject, "Calendar");
        var occurredOn = new DateOnly(2026, 1, 31);
        await SeedTransactionAsync(account, category.Id, TransactionDirection.Expense, 1m,
            occurredOn, "calendar");
        var cases = new[]
        {
            (Granularity: "week", Label: "2026-01-26 - 2026-02-01"),
            (Granularity: "month", Label: "2026-01"),
            (Granularity: "quarter", Label: "2026-Q1"),
            (Granularity: "year", Label: "2026")
        };

        foreach (var item in cases)
        {
            var response = await client.GetAsync(
                $"/api/reports/aggregate?dimension=period&granularity={item.Granularity}" +
                $"&from={occurredOn:yyyy-MM-dd}&to={occurredOn:yyyy-MM-dd}");
            var result = await response.Content.ReadFromJsonAsync<AggregationEnvelope>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(item.Label, Assert.Single(result!.Data!.Buckets).Label);
        }
    }

    [FunctionalFact]
    public async Task GivenInvalidDimensionGranularityRangeOrRole_WhenRequested_ThenRejected()
    {
        await using var factory = CreateFactory(maximumSpanDays: 3);
        using var user = factory.CreateClient();
        Authorize(user, Guid.NewGuid(), HeimdallRoles.User);
        var cases = new[]
        {
            (Url: $"/api/reports/aggregate?dimension=unknown&from={Start:yyyy-MM-dd}" +
                $"&to={Start:yyyy-MM-dd}", Expected: "Supported dimensions"),
            (Url: $"/api/reports/aggregate?dimension=period&granularity=hour" +
                $"&from={Start:yyyy-MM-dd}&to={Start:yyyy-MM-dd}",
                Expected: "Supported granularities"),
            (Url: $"/api/reports/aggregate?dimension=period&granularity=day" +
                $"&from={Start:yyyy-MM-dd}&to={Start.AddDays(3):yyyy-MM-dd}",
                Expected: "maximum aggregation range is 3 days")
        };

        foreach (var item in cases)
        {
            var response = await user.GetAsync(item.Url);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(item.Expected, await response.Content.ReadAsStringAsync(),
                StringComparison.OrdinalIgnoreCase);
        }

        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var valid = $"/api/reports/aggregate?dimension=category&from={Start:yyyy-MM-dd}" +
            $"&to={Start:yyyy-MM-dd}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(valid)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.GetAsync(valid)).StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<AggregationEnvelope> AggregateAsync(
        HttpClient client,
        string dimension,
        string suffix = "")
    {
        var response = await client.GetAsync(
            $"/api/reports/aggregate?dimension={dimension}&from={Start:yyyy-MM-dd}" +
            $"&to={Start:yyyy-MM-dd}{suffix}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AggregationEnvelope>())!;
    }

    private async Task<Category> SeedCategoryAsync(Guid subject, string name)
    {
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var category = new Category(user, name, DateTimeOffset.UtcNow);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category;
    }

    private async Task<ClassificationSeed> SeedClassificationAsync(Guid subject)
    {
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var root = new Category(user, "Root", DateTimeOffset.UtcNow);
        context.Categories.Add(root);
        await context.SaveChangesAsync();
        var child = new Category(user, "Child", DateTimeOffset.UtcNow, root);
        var counterparty = new Counterparty(user, "Merchant", DateTimeOffset.UtcNow);
        var tag = new Tag(user, "Coffee", DateTimeOffset.UtcNow);
        context.AddRange(child, counterparty, tag);
        await context.SaveChangesAsync();
        return new ClassificationSeed(root, child, counterparty, tag);
    }

    private async Task<Guid> SeedTransactionAsync(
        Guid accountId,
        long categoryId,
        TransactionDirection direction,
        decimal amount,
        DateOnly occurredOn,
        string description,
        Counterparty? counterparty = null,
        IReadOnlyCollection<Tag>? tags = null)
    {
        await using var context = CreateContext();
        var account = await context.FinancialAccounts
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == accountId);
        var category = await context.Categories.SingleAsync(item => item.Id == categoryId);
        Counterparty? attachedCounterparty = null;
        IReadOnlyCollection<Tag>? attachedTags = null;
        if (counterparty is not null)
        {
            attachedCounterparty = await context.Counterparties.SingleAsync(item =>
                item.PublicId == counterparty.PublicId);
        }

        if (tags is not null)
        {
            var ids = tags.Select(tag => tag.PublicId).ToArray();
            attachedTags = await context.Tags.Where(item => ids.Contains(item.PublicId)).ToArrayAsync();
        }

        var transaction = new FinancialTransaction(
            account.User,
            account,
            category,
            direction,
            amount,
            occurredOn,
            DateTimeOffset.UtcNow,
            description,
            attachedCounterparty,
            attachedTags);
        context.FinancialTransactions.Add(transaction);
        await context.SaveChangesAsync();
        return transaction.PublicId;
    }

    private async Task<Guid> SeedCardTransactionAsync(
        Guid cardId,
        long categoryId,
        TransactionDirection direction,
        decimal amount,
        DateOnly occurredOn,
        string description)
    {
        await using var context = CreateContext();
        var card = await context.CreditCards
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == cardId);
        var category = await context.Categories.SingleAsync(item => item.Id == categoryId);
        var transaction = new FinancialTransaction(
            card.User,
            card,
            category,
            direction,
            amount,
            occurredOn,
            DateTimeOffset.UtcNow,
            description);
        context.FinancialTransactions.Add(transaction);
        await context.SaveChangesAsync();
        return transaction.PublicId;
    }

    private async Task SeedTransferAsync(
        Guid outboundAccountId,
        Guid inboundAccountId,
        long categoryId,
        DateOnly occurredOn,
        decimal amount)
    {
        await using var context = CreateContext();
        var accounts = await context.FinancialAccounts
            .Include(item => item.User)
            .Include(item => item.Currency)
            .Where(item => item.PublicId == outboundAccountId || item.PublicId == inboundAccountId)
            .ToDictionaryAsync(item => item.PublicId);
        var category = await context.Categories.SingleAsync(item => item.Id == categoryId);
        var outbound = new FinancialTransaction(
            accounts[outboundAccountId].User,
            accounts[outboundAccountId],
            category,
            TransactionDirection.Expense,
            amount,
            occurredOn,
            DateTimeOffset.UtcNow);
        var inbound = new FinancialTransaction(
            accounts[inboundAccountId].User,
            accounts[inboundAccountId],
            category,
            TransactionDirection.Earning,
            amount,
            occurredOn,
            DateTimeOffset.UtcNow);
        context.FinancialTransactions.AddRange(outbound, inbound);
        await context.SaveChangesAsync();
        context.Transfers.Add(new Transfer(outbound, inbound, null, null, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
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

    private static async Task<Guid> CreateCardAsync(
        HttpClient client,
        string name,
        string currencyCode)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = "Example Bank",
            CurrencyCode = currencyCode,
            CreditLimit = 1000m,
            ClosingDay = 20,
            DueDay = 5,
            LastFourDigits = "1234"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private WebApplicationFactory<Program> CreateFactory(int maximumSpanDays = 366)
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
                services.RemoveAll<TransactionAggregationOptions>();
                services.AddSingleton(new TransactionAggregationOptions(maximumSpanDays));
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

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Aggregation Owner"
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
        ["FORTUNA_REPORT_MAX_RANGE_DAYS"] = "366",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record ClassificationSeed(
        Category Root,
        Category Child,
        Counterparty Counterparty,
        Tag Tag);

    private sealed record IdEnvelope(IdData? Data);
    private sealed record IdData(Guid Id);
    private sealed record AggregationEnvelope(AggregationData? Data);
    private sealed record AggregationData(
        string Dimension,
        string? Granularity,
        DateOnly From,
        DateOnly To,
        string DisplayCurrencyCode,
        bool IsFullyConverted,
        IReadOnlyCollection<BucketData> Buckets);
    private sealed record BucketData(
        string Label,
        decimal? Total,
        decimal? Share,
        DateOnly? PeriodStart,
        DateOnly? PeriodEnd,
        string DrillDownKey,
        bool IsFullyConverted,
        IReadOnlyCollection<ConversionData> Conversions);
    private sealed record ConversionData(
        string SourceCurrencyCode,
        decimal SourceAmount,
        DateOnly FigureDate,
        decimal? DisplayAmount,
        decimal? AppliedRate,
        DateOnly? RateDate,
        ExchangeRateSource? RateSource,
        string? UnconvertedReason);
}
