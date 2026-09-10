using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Exports;
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

public sealed class PersonalDataExportEndpointsTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly MemoryStorage storage = new();

    [FunctionalFact]
    public async Task GivenAccountOwner_WhenArchiveRequested_ThenPendingJobHandleIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        Authorize(client, subject);

        var response = await client.PostAsync("/api/me/data-export", null);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jobId = body.RootElement.GetProperty("data").GetProperty("jobId").GetGuid();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(0, body.RootElement.GetProperty("data").GetProperty("progress").GetInt32());
        await using var context = CreateContext();
        var export = await context.DataExports.Include(item => item.BackgroundJob)
            .SingleAsync(item => item.PublicId == jobId);
        Assert.Equal(DataExportKind.PersonalArchive, export.Kind);
        Assert.Equal(DataExportFormat.Zip, export.Format);
        Assert.Equal(PersonalDataExportJob.Type, export.BackgroundJob!.Type);
    }

    [FunctionalFact]
    public async Task GivenPendingArchive_WhenOwnerReadsHandle_ThenProgressIsReported()
    {
        var subject = Guid.NewGuid();
        var jobId = await SeedAsync(subject, DataExportStatus.Pending);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject);

        var response = await client.GetAsync($"/api/me/data-export/{jobId}");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((int)DataExportStatus.Pending,
            body.RootElement.GetProperty("data").GetProperty("status").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("data").GetProperty("progress").GetInt32());
    }

    [FunctionalFact]
    public async Task GivenForeignArchive_WhenHandleRead_ThenNotFoundHidesItsExistence()
    {
        var jobId = await SeedAsync(Guid.NewGuid(), DataExportStatus.Pending);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid());

        var response = await client.GetAsync($"/api/me/data-export/{jobId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(PersonalDataExportMessages.NotFound,
            await response.Content.ReadAsStringAsync());
    }

    [FunctionalFact]
    public async Task GivenFailedOrExpiredArchive_WhenHandleRead_ThenFailureOrGoneIsReported()
    {
        var subject = Guid.NewGuid();
        var failedId = await SeedAsync(subject, DataExportStatus.Failed);
        var expiredId = await SeedAsync(subject, DataExportStatus.Completed, expired: true);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject);

        var failed = await client.GetAsync($"/api/me/data-export/{failedId}");
        var failedBody = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
        var expired = await client.GetAsync($"/api/me/data-export/{expiredId}");

        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        Assert.Equal(PersonalDataExportMessages.GenerationFailed,
            failedBody.RootElement.GetProperty("data").GetProperty("failureReason").GetString());
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        Assert.Contains(PersonalDataExportMessages.Expired,
            await expired.Content.ReadAsStringAsync());

        var retry = await client.PostAsync("/api/me/data-export", null);
        var retryBody = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.NotEqual(failedId,
            retryBody.RootElement.GetProperty("data").GetProperty("jobId").GetGuid());
    }

    [FunctionalFact]
    public async Task GivenCompletedArchive_WhenOwnerDownloads_ThenZipBytesAreReturned()
    {
        var subject = Guid.NewGuid();
        var jobId = await SeedAsync(subject, DataExportStatus.Completed);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject);

        var response = await client.GetAsync($"/api/me/data-export/{jobId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 80, 75, 3, 4 }, await response.Content.ReadAsByteArrayAsync());
    }

    [FunctionalFact]
    public async Task GivenAnonymousCaller_WhenArchiveRequested_ThenUnauthorizedIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/me/data-export", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task<Guid> SeedAsync(
        Guid externalSubject,
        DataExportStatus status,
        bool expired = false)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var user = await context.UserProfiles.SingleOrDefaultAsync(
            item => item.ExternalSubject == externalSubject.ToString("D"));
        if (user is null)
        {
            user = new UserProfile(externalSubject, "Portable Owner", currency, Now.AddHours(-2));
            context.UserProfiles.Add(user);
            await context.SaveChangesAsync();
        }
        var created = expired ? Now.AddHours(-2) : Now.AddMinutes(-10);
        var expires = expired ? Now.AddHours(-1) : Now.AddHours(23);
        var export = new DataExport(
            user,
            DataExportFormat.Zip,
            "und",
            "fortuna-personal-data.zip",
            "{\"archiveSchemaVersion\":1}",
            created,
            expires,
            DataExportKind.PersonalArchive);
        var background = BackgroundJob.Create(
            PersonalDataExportJob.Type,
            "{}",
            $"personal-data-export:{export.PublicId:N}",
            null,
            created);
        export.AttachBackgroundJob(background);
        context.AddRange(background, export);
        if (status != DataExportStatus.Pending)
        {
            export.Start(created.AddMinutes(1));
        }
        if (status == DataExportStatus.Completed)
        {
            var key = $"exports/{user.PublicId:N}/{export.PublicId:N}.zip";
            export.Complete(1, "application/zip", key, created.AddMinutes(2));
            await using var content = new MemoryStream([80, 75, 3, 4], writable: false);
            await storage.WriteAsync(key, content, CancellationToken.None);
        }
        else if (status == DataExportStatus.Failed)
        {
            export.Fail(PersonalDataExportMessages.GenerationFailed, created.AddMinutes(2));
        }
        await context.SaveChangesAsync();
        return export.PublicId;
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
                services.RemoveAll<IAttachmentStore>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString()));
                services.AddSingleton<IAttachmentStore>(storage);
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

    private static void Authorize(HttpClient client, Guid subject)
    {
        var identity = new FortunaIdentity(subject, (int)HeimdallRoles.User, Guid.NewGuid(), [])
        {
            DisplayName = "Portable Owner"
        };
        var configuration = new JwtConfiguration(
            3600, Issuer, Audience, Secret, new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtHandler().CreateToken(configuration));
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-personal-export-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-personal-export-logs"),
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

    private sealed class MemoryStorage : IAttachmentStore
    {
        private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);
        public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            values[key] = copy.ToArray();
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
            values.TryGetValue(key, out var content)
                ? Task.FromResult<Stream>(new MemoryStream(content, writable: false))
                : throw new AttachmentObjectNotFoundException(key);
        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            values.Remove(key);
            return Task.CompletedTask;
        }
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
