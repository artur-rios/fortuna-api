using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Security;
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

public sealed class GoalDefinitionTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fortuna")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    [FunctionalFact]
    public async Task GivenAccount_WhenGoalCreated_ThenCurrentProgressIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var accountId = await CreateAccountAsync(client, 1_000m);

        var response = await CreateGoalAsync(client, 2_000m, [accountId], []);
        var goal = (await response.Content.ReadFromJsonAsync<GoalEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Home", goal.Name);
        Assert.Equal(1_000m, goal.CurrentProgress.CurrentAmount);
        Assert.Equal(1_000m, goal.CurrentProgress.Remaining);
        Assert.Equal(0.5m, goal.CurrentProgress.ProportionReached);
        Assert.False(goal.CurrentProgress.IsReached);
        Assert.True(goal.CurrentProgress.IsFullyConverted);
        Assert.Equal(accountId, Assert.Single(goal.Accounts).Id);
    }

    [FunctionalFact]
    public async Task GivenGoal_WhenManaged_ThenCrudAndSoftDeletionAreApplied()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var accountId = await CreateAccountAsync(client, 100m);
        var investmentId = await CreateInvestmentAsync(client);
        var create = await CreateGoalAsync(client, 500m, [accountId], []);
        var created = (await create.Content.ReadFromJsonAsync<GoalEnvelope>())!.Data!;

        var get = await client.GetAsync($"/api/goals/{created.Id}");
        var update = await client.PutAsJsonAsync($"/api/goals/{created.Id}", new
        {
            Name = "Retirement",
            TargetAmount = 1_000m,
            CurrencyCode = "BRL",
            TargetDate = new DateOnly(2028, 1, 1),
            AccountIds = Array.Empty<Guid>(),
            InvestmentIds = new[] { investmentId }
        });
        var updated = (await update.Content.ReadFromJsonAsync<GoalEnvelope>())!.Data!;
        var list = (await client.GetFromJsonAsync<GoalListEnvelope>("/api/goals"))!.Data!;
        var delete = await client.DeleteAsync($"/api/goals/{created.Id}");
        var hidden = await client.GetAsync($"/api/goals/{created.Id}");
        var deletedResponse = await client.GetAsync(
            $"/api/goals/{created.Id}?includeDeleted=true");
        var deleted = (await deletedResponse.Content.ReadFromJsonAsync<GoalEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Retirement", updated.Name);
        Assert.Equal(investmentId, Assert.Single(updated.Investments).Id);
        Assert.Empty(updated.Accounts);
        Assert.Equal(created.Id, Assert.Single(list.Goals).Id);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deletedResponse.StatusCode);
        Assert.True(deleted.IsDeleted);
    }

    [FunctionalFact]
    public async Task GivenInvalidOrForeignResources_WhenGoalCreated_ThenRequestIsRejected()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var foreignAccount = await CreateAccountAsync(other, 100m);
        var foreignInvestment = await CreateInvestmentAsync(other);

        var zero = await CreateGoalAsync(owner, 0m, [Guid.NewGuid()], []);
        var empty = await CreateGoalAsync(owner, 100m, [], []);
        var past = await CreateGoalAsync(
            owner, 100m, [Guid.NewGuid()], [], new DateOnly(2026, 9, 6));
        var account = await CreateGoalAsync(owner, 100m, [foreignAccount], []);
        var investment = await CreateGoalAsync(owner, 100m, [], [foreignInvestment]);

        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Contains(GoalMessages.TargetAmountMustBePositive,
            await zero.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains(GoalMessages.ResourcesRequired,
            await empty.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Contains(GoalMessages.TargetDateMustBeFuture,
            await past.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, account.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, investment.StatusCode);
        await using var context = CreateContext();
        Assert.False(await context.Goals.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenForeignOrUnauthorizedGoal_WhenAccessed_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        var accountId = await CreateAccountAsync(owner, 100m);
        var create = await CreateGoalAsync(owner, 500m, [accountId], []);
        var goal = (await create.Content.ReadFromJsonAsync<GoalEnvelope>())!.Data!;
        using var other = factory.CreateClient();
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var foreignGet = await other.GetAsync($"/api/goals/{goal.Id}");
        var foreignDelete = await other.DeleteAsync($"/api/goals/{goal.Id}");
        var unauthorized = await anonymous.GetAsync("/api/goals");
        var forbidden = await administrator.GetAsync("/api/goals");

        Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

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
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
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

    private static Task<HttpResponseMessage> CreateGoalAsync(
        HttpClient client,
        decimal targetAmount,
        IReadOnlyCollection<Guid> accountIds,
        IReadOnlyCollection<Guid> investmentIds,
        DateOnly? targetDate = null) => client.PostAsJsonAsync("/api/goals", new
        {
            Name = "Home",
            TargetAmount = targetAmount,
            CurrencyCode = "BRL",
            TargetDate = targetDate ?? new DateOnly(2027, 1, 1),
            AccountIds = accountIds,
            InvestmentIds = investmentIds
        });

    private static async Task<Guid> CreateAccountAsync(HttpClient client, decimal openingBalance)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new
        {
            Name = $"Account {Guid.NewGuid():N}",
            AccountType = FinancialAccountType.Savings,
            CurrencyCode = "BRL",
            OpeningBalance = openingBalance
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static async Task<Guid> CreateInvestmentAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/investments", new
        {
            Instrument = $"Investment {Guid.NewGuid():N}",
            InvestmentType = InvestmentType.FixedIncome,
            CurrencyCode = "BRL"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdEnvelope>())!.Data!.Id;
    }

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Account Owner"
        };
        var configuration = new JwtConfiguration(
            3600, Issuer, Audience, Secret, new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtHandler().CreateToken(configuration));
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
    private sealed record GoalEnvelope(GoalData? Data);
    private sealed record GoalListEnvelope(GoalListData? Data);
    private sealed record GoalListData(IReadOnlyList<GoalData> Goals);
    private sealed record GoalData(
        Guid Id,
        string Name,
        decimal TargetAmount,
        string CurrencyCode,
        DateOnly TargetDate,
        IReadOnlyList<GoalResourceData> Accounts,
        IReadOnlyList<GoalResourceData> Investments,
        GoalProgressData CurrentProgress,
        bool IsDeleted);
    private sealed record GoalResourceData(Guid Id, string Name);
    private sealed record GoalProgressData(
        decimal? CurrentAmount,
        decimal? Remaining,
        decimal? ProportionReached,
        bool? IsReached,
        bool IsFullyConverted);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
