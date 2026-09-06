using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
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

public sealed class ConnectionCreationTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private const string AccessToken = "pluggy-access-token-never-stored-in-plain-text";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 7, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fortuna")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    [FunctionalFact]
    public async Task GivenValidPluggyItem_WhenConnected_ThenOnlyEncryptedTokenIsStored()
    {
        var gateway = Gateway(PluggyConnectionValidationOutcome.Succeeded);
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await ConnectAsync(client);
        var connection = (await response.Content.ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(TransactionSourceType.Pluggy, connection.DataSourceType);
        Assert.Equal("Nubank", connection.Institution);
        Assert.Equal(ConnectionStatus.Active, connection.Status);
        Assert.DoesNotContain(AccessToken, await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        var stored = await context.Connections.SingleAsync();
        Assert.NotEmpty(stored.AccessTokenCipher);
        Assert.NotEqual(AccessToken, System.Text.Encoding.UTF8.GetString(stored.AccessTokenCipher));
        Assert.NotEmpty(await context.DataProtectionKeys.ToListAsync());
    }

    [FunctionalTheory]
    [InlineData(PluggyConnectionValidationOutcome.InvalidReference, HttpStatusCode.BadRequest,
        ConnectionMessages.InvalidReference)]
    [InlineData(PluggyConnectionValidationOutcome.Unavailable, HttpStatusCode.ServiceUnavailable,
        ConnectionMessages.SourceUnavailable)]
    [InlineData(PluggyConnectionValidationOutcome.NotConfigured, HttpStatusCode.NotFound,
        ConnectionMessages.SourceNotAvailable)]
    public async Task GivenPluggyRejection_WhenConnected_ThenNothingIsStored(
        PluggyConnectionValidationOutcome outcome,
        HttpStatusCode expectedStatus,
        string expectedMessage)
    {
        await using var factory = CreateFactory(Gateway(outcome));
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await ConnectAsync(client);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains(expectedMessage, await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.False(await context.Connections.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenCredentialInRequest_WhenConnected_ThenItIsRejectedBeforeExternalCall()
    {
        var gateway = Gateway(PluggyConnectionValidationOutcome.Succeeded);
        await using var factory = CreateFactory(gateway);
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/connections", new
        {
            DataSource = "pluggy",
            ExternalReference = Guid.NewGuid(),
            Username = "customer",
            Password = "bank-password",
            MfaToken = "123456"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ConnectionMessages.BankCredentialRejected,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, gateway.CallCount);
        await using var context = CreateContext();
        Assert.False(await context.Connections.AnyAsync());
    }

    [FunctionalFact]
    public async Task GivenSameReference_WhenConnected_ThenDuplicateIsScopedToOwner()
    {
        var gateway = Gateway(PluggyConnectionValidationOutcome.Succeeded);
        await using var factory = CreateFactory(gateway);
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, Guid.NewGuid(), HeimdallRoles.User);
        Authorize(other, Guid.NewGuid(), HeimdallRoles.User);
        var reference = Guid.NewGuid();

        var created = await ConnectAsync(owner, reference);
        var duplicate = await ConnectAsync(owner, reference);
        var isolated = await ConnectAsync(other, reference);
        var existing = (await duplicate.Content
            .ReadFromJsonAsync<ConnectionEnvelope>())!.Data!;

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.NotEqual(Guid.Empty, existing.Id);
        Assert.Equal(HttpStatusCode.Created, isolated.StatusCode);
        Assert.Equal(3, gateway.CallCount);
        await using var context = CreateContext();
        Assert.Equal(2, await context.Connections.CountAsync());
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenConnected_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory(Gateway(
            PluggyConnectionValidationOutcome.Succeeded));
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var unauthorized = await ConnectAsync(anonymous);
        var forbidden = await ConnectAsync(administrator);

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

    private WebApplicationFactory<Program> CreateFactory(StubPluggyGateway gateway)
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
                services.RemoveAll<IPluggyConnectionGateway>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<IPluggyConnectionGateway>(gateway);
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

    private static Task<HttpResponseMessage> ConnectAsync(
        HttpClient client,
        Guid? externalReference = null) => client.PostAsJsonAsync("/api/connections", new
        {
            DataSource = "pluggy",
            ExternalReference = externalReference ?? Guid.NewGuid()
        });

    private static StubPluggyGateway Gateway(PluggyConnectionValidationOutcome outcome) => new(
        outcome == PluggyConnectionValidationOutcome.Succeeded
            ? new(outcome, "Nubank", AccessToken)
            : new(outcome));

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
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
        ["FORTUNA_PLUGGY_CLIENT_ID"] = "client",
        ["FORTUNA_PLUGGY_CLIENT_SECRET"] = "secret",
        ["FORTUNA_PLUGGY_BASE_URL"] = "https://pluggy.example"
    };

    private sealed record ConnectionEnvelope(ConnectionData? Data);
    private sealed record ConnectionData(
        Guid Id,
        TransactionSourceType DataSourceType,
        string ExternalReference,
        string Institution,
        ConnectionStatus Status);

    private sealed class StubPluggyGateway(PluggyConnectionValidation result)
        : IPluggyConnectionGateway
    {
        public int CallCount { get; private set; }

        public Task<PluggyConnectionValidation> ValidateAsync(
            string externalReference,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
