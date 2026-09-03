using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Security;
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

public sealed class UserProfileProvisioningTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenFirstAuthenticatedRequest_WhenProfileIsRead_ThenProfileIsProvisionedFromToken()
    {
        await using var factory = CreateFactory("USD");
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        Authorize(client, TokenFor(subject, "Ada Lovelace"));

        var response = await client.GetFromJsonAsync<ProfileEnvelope>("/api/me");

        Assert.NotNull(response?.Data);
        Assert.Equal("Ada Lovelace", response.Data.DisplayName);
        Assert.Equal("USD", response.Data.DisplayCurrency);
        Assert.False(response.Data.DisplayCurrencyRequiresConfirmation);
        await using var context = CreateContext();
        Assert.Equal(1, await context.UserProfiles.CountAsync(
            x => x.ExternalSubject == subject.ToString("D")));
    }

    [FunctionalFact]
    public async Task GivenExistingProfile_WhenAuthenticatedAgain_ThenExistingProfileIsReused()
    {
        await using var factory = CreateFactory("BRL");
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        Authorize(client, TokenFor(subject, "First Name"));
        var first = await client.GetFromJsonAsync<ProfileEnvelope>("/api/me");
        Authorize(client, TokenFor(subject, "Changed Upstream Name"));

        var second = await client.GetFromJsonAsync<ProfileEnvelope>("/api/me");

        Assert.Equal(first!.Data!.Id, second!.Data!.Id);
        Assert.Equal("First Name", second.Data.DisplayName);
        await using var context = CreateContext();
        Assert.Equal(1, await context.UserProfiles.CountAsync(
            x => x.ExternalSubject == subject.ToString("D")));
    }

    [FunctionalFact]
    public async Task GivenConcurrentFirstRequests_WhenProfileIsProvisioned_ThenExactlyOneProfileExists()
    {
        await using var factory = CreateFactory("BRL");
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        Authorize(client, TokenFor(subject, "Concurrent User"));

        var profiles = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.GetFromJsonAsync<ProfileEnvelope>("/api/me")));

        Assert.Single(profiles.Select(x => x!.Data!.Id).Distinct());
        await using var context = CreateContext();
        Assert.Equal(1, await context.UserProfiles.CountAsync(
            x => x.ExternalSubject == subject.ToString("D")));
    }

    [FunctionalFact]
    public async Task GivenTokenWithoutSubject_WhenProfileIsRequested_ThenUnauthorizedCreatesNothing()
    {
        await using var factory = CreateFactory("BRL");
        using var client = factory.CreateClient();
        await using var beforeContext = CreateContext();
        var before = await beforeContext.UserProfiles.CountAsync();
        Authorize(client, TokenWithoutSubject("Missing Subject"));

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var afterContext = CreateContext();
        Assert.Equal(before, await afterContext.UserProfiles.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenDefaultCurrencyMissing_WhenProfileIsProvisioned_ThenLocaleCurrencyNeedsConfirmation()
    {
        await using var factory = CreateFactory(null);
        using var client = factory.CreateClient();
        Authorize(client, TokenFor(Guid.NewGuid(), "Locale User"));

        var response = await client.GetFromJsonAsync<ProfileEnvelope>("/api/me");

        Assert.Equal("BRL", response!.Data!.DisplayCurrency);
        Assert.True(response.Data.DisplayCurrencyRequiresConfirmation);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory(string? defaultCurrency)
    {
        foreach (var setting in ValidSettings(defaultCurrency))
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
                services.AddDbContext<AppDbContext>(databaseOptions =>
                    databaseOptions.UseNpgsql(database.GetConnectionString()));
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

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string TokenFor(Guid subject, string displayName)
    {
        var identity = new FortunaIdentity(subject, (int)HeimdallRoles.User, Guid.NewGuid(), [])
        {
            DisplayName = displayName
        };
        var configuration = new JwtConfiguration(
            3600, Issuer, Audience, Secret, new FortunaIdentityMapper().ToClaims(identity));

        return new JwtHandler().CreateToken(configuration);
    }

    private static string TokenWithoutSubject(string displayName)
    {
        var claims = new Dictionary<string, string>
        {
            [FortunaIdentityMapper.RoleClaim] = ((int)HeimdallRoles.User).ToString(),
            [FortunaIdentityMapper.DisplayNameClaim] = displayName
        };

        return new JwtHandler().CreateToken(
            new JwtConfiguration(3600, Issuer, Audience, Secret, claims));
    }

    private static Dictionary<string, string?> ValidSettings(string? defaultCurrency) => new()
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
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = defaultCurrency,
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed record ProfileEnvelope(ProfileData? Data);
    private sealed record ProfileData(
        Guid Id,
        string DisplayName,
        string DisplayCurrency,
        bool DisplayCurrencyRequiresConfirmation);
}
