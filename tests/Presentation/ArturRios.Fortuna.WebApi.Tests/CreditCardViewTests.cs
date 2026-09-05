using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
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

public sealed class CreditCardViewTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenLiveChargesAndCredit_WhenReadById_ThenUsedLimitIsDerived()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Rewards", creditLimit: 1000m);
        await AddMovementAsync(card.Id, TransactionDirection.Expense, 700.55m);
        await AddMovementAsync(card.Id, TransactionDirection.Earning, 200m);
        await AddMovementAsync(card.Id, TransactionDirection.Expense, 50m, isDeleted: true);

        var response = await client.GetAsync($"/api/credit-cards/{card.Id}");
        var envelope = await response.Content.ReadFromJsonAsync<CreditCardEnvelope>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(card.Id, envelope?.Data?.Id);
        Assert.Equal(500.55m, envelope?.Data?.UsedAmount);
        Assert.Equal(499.45m, envelope?.Data?.AvailableAmount);
        Assert.Equal(0m, envelope?.Data?.OverageAmount);
        Assert.Contains(CreditCardMessages.RetrievedSuccessfully, envelope!.Messages);
    }

    [FunctionalFact]
    public async Task GivenChargesExceedLimit_WhenReadById_ThenOverageIsSeparate()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Over Limit", creditLimit: 1000m);
        await AddMovementAsync(card.Id, TransactionDirection.Expense, 1250m);

        var result = await client.GetFromJsonAsync<CreditCardEnvelope>(
            $"/api/credit-cards/{card.Id}");

        Assert.Equal(1250m, result?.Data?.UsedAmount);
        Assert.Equal(0m, result?.Data?.AvailableAmount);
        Assert.Equal(250m, result?.Data?.OverageAmount);
    }

    [FunctionalFact]
    public async Task GivenNoCharges_WhenReadById_ThenFullLimitIsAvailable()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var card = await CreateCardAsync(client, "Unused", creditLimit: 2500m);

        var result = await client.GetFromJsonAsync<CreditCardEnvelope>(
            $"/api/credit-cards/{card.Id}");

        Assert.Equal(0m, result?.Data?.UsedAmount);
        Assert.Equal(2500m, result?.Data?.AvailableAmount);
        Assert.Equal(0m, result?.Data?.OverageAmount);
    }

    [FunctionalFact]
    public async Task GivenMissingForeignOrDeletedCard_WhenRead_ThenSameNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var foreignCard = await CreateCardAsync(owner, "Private");
        var deletedCard = await CreateCardAsync(owner, "Archived");
        await using (var context = CreateContext())
        {
            var storedCard = await context.CreditCards.SingleAsync(card => card.PublicId == deletedCard.Id);
            storedCard.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);

        var foreign = await other.GetAsync($"/api/credit-cards/{foreignCard.Id}");
        var deleted = await owner.GetAsync($"/api/credit-cards/{deletedCard.Id}");
        var missing = await other.GetAsync($"/api/credit-cards/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains(CreditCardMessages.NotFound, await foreign.Content.ReadAsStringAsync());
        Assert.Contains(CreditCardMessages.NotFound, await deleted.Content.ReadAsStringAsync());
        Assert.Contains(CreditCardMessages.NotFound, await missing.Content.ReadAsStringAsync());
    }

    [FunctionalFact]
    public async Task GivenFiltersAndUsedSort_WhenListed_ThenOnlyOwnedLiveMatchesAreReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var first = await CreateCardAsync(client, "Reserve One", "Example Bank", "USD", 1000m);
        var second = await CreateCardAsync(client, "Reserve Two", "Example Bank", "USD", 2000m);
        await CreateCardAsync(client, "Daily", "Other Bank", "BRL", 3000m);
        await AddMovementAsync(first.Id, TransactionDirection.Expense, 100m);
        await AddMovementAsync(second.Id, TransactionDirection.Expense, 500m);
        using var foreignClient = factory.CreateClient();
        Authorize(foreignClient, Guid.NewGuid(), HeimdallRoles.User);
        await CreateCardAsync(foreignClient, "Reserve Foreign", "Example Bank", "USD", 4000m);

        var response = await client.GetAsync(
            "/api/credit-cards?Name=reserve&Issuer=AMPLE&CurrencyCode=usd" +
            "&SortBy=UsedAmount&Descending=true&PageNumber=1&PageSize=10");
        var page = await response.Content.ReadFromJsonAsync<CreditCardPage>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, page?.TotalItems);
        Assert.Equal([500m, 100m], page!.Data.Select(card => card.UsedAmount));
        Assert.Contains(CreditCardMessages.ListedSuccessfully, page.Messages);
    }

    [FunctionalFact]
    public async Task GivenDeletedCard_WhenListed_ThenItIsExcluded()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var live = await CreateCardAsync(client, "Live");
        var archived = await CreateCardAsync(client, "Archived");
        await using (var context = CreateContext())
        {
            var card = await context.CreditCards.SingleAsync(item => item.PublicId == archived.Id);
            card.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        var page = await client.GetFromJsonAsync<CreditCardPage>("/api/credit-cards");

        Assert.Equal(live.Id, Assert.Single(page!.Data).Id);
    }

    [FunctionalFact]
    public async Task GivenUnsupportedSortOrFilter_WhenListed_ThenBadRequestNamesTheField()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var sort = await client.GetAsync("/api/credit-cards?SortBy=AvailableAmount");
        var filter = await client.GetAsync("/api/credit-cards?UsedAmount=10");

        Assert.Equal(HttpStatusCode.BadRequest, sort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, filter.StatusCode);
        Assert.Contains("SortBy", await sort.Content.ReadAsStringAsync());
        Assert.Contains("UsedAmount", await filter.Content.ReadAsStringAsync());
    }

    [FunctionalFact]
    public async Task GivenOversizedPage_WhenListed_ThenPageSizeIsClampedAndReported()
    {
        await using var factory = CreateFactory(maximumPageSize: 2);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        await CreateCardAsync(client, "One");
        await CreateCardAsync(client, "Two");
        await CreateCardAsync(client, "Three");

        var page = await client.GetFromJsonAsync<CreditCardPage>("/api/credit-cards?PageSize=999");

        Assert.Equal(2, page?.PageSize);
        Assert.Equal(3, page?.TotalItems);
        Assert.Equal(2, page?.Data.Count);
    }

    [FunctionalFact]
    public async Task GivenNoTokenOrAdministrator_WhenViewed_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var anonymousList = await anonymous.GetAsync("/api/credit-cards");
        var administratorList = await administrator.GetAsync("/api/credit-cards");
        var anonymousDetail = await anonymous.GetAsync($"/api/credit-cards/{Guid.NewGuid()}");
        var administratorDetail = await administrator.GetAsync($"/api/credit-cards/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorList.StatusCode);
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

    private async Task AddMovementAsync(
        Guid cardId,
        TransactionDirection direction,
        decimal amount,
        bool isDeleted = false)
    {
        await using var context = CreateContext();
        var card = await context.CreditCards
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleAsync(item => item.PublicId == cardId);
        var category = await context.Categories.SingleOrDefaultAsync(item =>
            item.UserId == card.User.Id && item.NormalizedName == "GENERAL" && !item.IsDeleted);
        category ??= new Category(card.User, "General", DateTimeOffset.UtcNow);
        var transaction = new FinancialTransaction(
            card.User,
            card,
            category,
            direction,
            amount,
            new DateOnly(2026, 9, 4),
            DateTimeOffset.UtcNow);
        if (isDeleted)
        {
            transaction.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.FinancialTransactions.Add(transaction);
        await context.SaveChangesAsync();
    }

    private static async Task<CreditCardData> CreateCardAsync(
        HttpClient client,
        string name,
        string issuer = "Example Bank",
        string currencyCode = "BRL",
        decimal creditLimit = 1000m)
    {
        var response = await client.PostAsJsonAsync("/api/credit-cards", new
        {
            Name = name,
            Issuer = issuer,
            CurrencyCode = currencyCode,
            CreditLimit = creditLimit,
            ClosingDay = 20,
            DueDay = 5,
            LastFourDigits = "1234"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreditCardEnvelope>())!.Data!;
    }

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Account Owner"
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
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
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

    private sealed record CreditCardEnvelope(
        CreditCardData? Data,
        IReadOnlyCollection<string> Messages);

    private sealed record CreditCardPage(
        IReadOnlyList<CreditCardData> Data,
        IReadOnlyCollection<string> Messages,
        int PageNumber,
        int PageSize,
        int TotalItems,
        int TotalPages);

    private sealed record CreditCardData(
        Guid Id,
        string Name,
        string Issuer,
        string CurrencyCode,
        decimal CreditLimit,
        decimal UsedAmount,
        decimal AvailableAmount,
        decimal OverageAmount,
        short ClosingDay,
        short DueDay,
        string? LastFourDigits,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
