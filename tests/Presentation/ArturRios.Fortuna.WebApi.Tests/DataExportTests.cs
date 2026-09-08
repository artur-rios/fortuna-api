using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Attachments;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.Shared.Jobs;
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

public sealed class DataExportTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateOnly Today = new(2026, 9, 8);
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string storageRoot = Path.Combine(
        Path.GetTempPath(), "fortuna-export-tests", Guid.NewGuid().ToString("N"));

    [FunctionalFact]
    public async Task GivenOwnedLiveRows_WhenCsvExported_ThenExactCurrenciesAndNamesAreIncluded()
    {
        var subject = Guid.NewGuid();
        await SeedMixedRowsAsync(subject);
        await using var factory = CreateFactory(100);
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsJsonAsync("/api/exports", Command(
            "csv", ["description", "amount", "attachmentNames"]));
        var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("currencyCode", text);
        Assert.Contains("Owner BRL", text);
        Assert.Contains("Owner USD", text);
        Assert.Contains("12,34", text);
        Assert.Contains("2,5", text);
        Assert.Contains("BRL", text);
        Assert.Contains("USD", text);
        Assert.Contains("receipt.pdf", text);
        Assert.DoesNotContain("secret/object", text);
        Assert.DoesNotContain("Deleted row", text);
        Assert.DoesNotContain("Other owner", text);
    }

    [FunctionalFact]
    public async Task GivenFormatsEmptyOrInvalidAccess_WhenExportRequested_ThenEachAlternativeIsExplicit()
    {
        var subject = Guid.NewGuid();
        var emptySubject = Guid.NewGuid();
        await SeedSingleRowAsync(subject);
        await SeedEmptyProfileAsync(emptySubject);
        await using var factory = CreateFactory(100);
        using var owner = factory.CreateClient();
        Authorize(owner, subject, HeimdallRoles.User);
        using var emptyOwner = factory.CreateClient();
        Authorize(emptyOwner, emptySubject, HeimdallRoles.User);
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var excel = await owner.PostAsJsonAsync(
            "/api/exports", Command("xlsx", ["description", "amount"]));
        var pdf = await owner.PostAsJsonAsync(
            "/api/exports", Command("pdf", ["description", "amount"]));
        var empty = await emptyOwner.PostAsJsonAsync(
            "/api/exports", Command("csv", ["id", "amount"]));
        var invalid = await owner.PostAsJsonAsync(
            "/api/exports", Command("xml", ["id"]));
        var anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/exports", Command("csv", ["id"]));
        var forbidden = await administrator.PostAsJsonAsync(
            "/api/exports", Command("csv", ["id"]));

        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        Assert.Equal("PK", Encoding.ASCII.GetString(
            (await excel.Content.ReadAsByteArrayAsync())[..2]));
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(
            await pdf.Content.ReadAsByteArrayAsync()));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var emptyText = Encoding.UTF8.GetString(await empty.Content.ReadAsByteArrayAsync());
        Assert.Contains("id", emptyText);
        Assert.Contains("amount", emptyText);
        Assert.Single(emptyText.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(DataExportMessages.FormatUnsupported,
            await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenResultAboveThreshold_WhenExportRequested_ThenJobProducesStoredFile()
    {
        var subject = Guid.NewGuid();
        await SeedTwoRowsAsync(subject);
        await using var factory = CreateFactory(1);
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var response = await client.PostAsJsonAsync(
            "/api/exports", Command("csv", ["description", "amount"]));
        var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = responseJson.RootElement.GetProperty("data");
        var exportId = data.GetProperty("exportId").GetGuid();
        var jobId = data.GetProperty("jobId").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<BackgroundJobProcessor>()
                .ProcessAsync(jobId, CancellationToken.None);
        }

        await using var context = CreateContext();
        var export = await context.DataExports.SingleAsync(item => item.PublicId == exportId);
        var job = await context.BackgroundJobs.SingleAsync(item => item.Id == jobId);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(DataExportStatus.Completed, export.Status);
        Assert.Equal(2, export.RowCount);
        Assert.Equal(BackgroundJobState.Succeeded, job.State);
        Assert.NotNull(export.StorageKey);
        var stored = Path.Combine(storageRoot,
            export.StorageKey!.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(stored));
        Assert.Contains("First row", await File.ReadAllTextAsync(stored));
        Assert.Contains("Second row", await File.ReadAllTextAsync(stored));
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await database.DisposeAsync();
        if (Directory.Exists(storageRoot))
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    private static RequestDataExportCommand Command(
        string format,
        IReadOnlyCollection<string> columns) => new()
        {
            RecordSet = "transactions",
            Columns = columns,
            Format = format,
            Locale = "pt-BR"
        };

    private async Task SeedMixedRowsAsync(Guid subject)
    {
        await using var context = CreateContext();
        var brl = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var usd = await context.Currencies.SingleAsync(item => item.Code == "USD");
        var user = new UserProfile(subject, "Owner", brl, DateTimeOffset.UtcNow);
        var brlAccount = new FinancialAccount(user, "BRL", null,
            FinancialAccountType.Checking, brl, 0m, DateTimeOffset.UtcNow);
        var usdAccount = new FinancialAccount(user, "USD", null,
            FinancialAccountType.Checking, usd, 0m, DateTimeOffset.UtcNow);
        var category = new Category(user, "General", DateTimeOffset.UtcNow);
        var brlRow = new FinancialTransaction(user, brlAccount, category,
            TransactionDirection.Expense, 12.34m, Today, DateTimeOffset.UtcNow,
            "Owner BRL");
        var usdRow = new FinancialTransaction(user, usdAccount, category,
            TransactionDirection.Expense, 2.5m, Today, DateTimeOffset.UtcNow,
            "Owner USD");
        var deleted = new FinancialTransaction(user, brlAccount, category,
            TransactionDirection.Expense, 50m, Today, DateTimeOffset.UtcNow,
            "Deleted row");
        deleted.SoftDelete(DateTimeOffset.UtcNow);
        var attachment = new Attachment(
            brlRow, "receipt.pdf", "application/pdf", 3, "secret/object",
            DateTimeOffset.UtcNow);

        var other = new UserProfile(Guid.NewGuid(), "Other", brl, DateTimeOffset.UtcNow);
        var otherAccount = new FinancialAccount(other, "Other", null,
            FinancialAccountType.Checking, brl, 0m, DateTimeOffset.UtcNow);
        var otherCategory = new Category(other, "Other", DateTimeOffset.UtcNow);
        var otherRow = new FinancialTransaction(other, otherAccount, otherCategory,
            TransactionDirection.Expense, 999m, Today, DateTimeOffset.UtcNow,
            "Other owner");
        context.AddRange(user, brlAccount, usdAccount, category, brlRow, usdRow,
            deleted, attachment, other, otherAccount, otherCategory, otherRow);
        await context.SaveChangesAsync();
    }

    private async Task SeedSingleRowAsync(Guid subject) =>
        await SeedRowsAsync(subject, ["One row"]);

    private async Task SeedTwoRowsAsync(Guid subject) =>
        await SeedRowsAsync(subject, ["First row", "Second row"]);

    private async Task SeedRowsAsync(Guid subject, IReadOnlyCollection<string> descriptions)
    {
        await using var context = CreateContext();
        var brl = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var user = new UserProfile(subject, "Owner", brl, DateTimeOffset.UtcNow);
        var account = new FinancialAccount(user, "Checking", null,
            FinancialAccountType.Checking, brl, 0m, DateTimeOffset.UtcNow);
        var category = new Category(user, "General", DateTimeOffset.UtcNow);
        context.AddRange(user, account, category);
        foreach (var description in descriptions)
        {
            context.FinancialTransactions.Add(new FinancialTransaction(
                user, account, category, TransactionDirection.Expense,
                10m, Today, DateTimeOffset.UtcNow, description));
        }

        await context.SaveChangesAsync();
    }

    private async Task SeedEmptyProfileAsync(Guid subject)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        context.UserProfiles.Add(new UserProfile(
            subject, "Empty Owner", currency, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> CreateFactory(int synchronousThreshold)
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
                services.RemoveAll<DataExportOptions>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(new DataExportOptions(
                    synchronousThreshold, TimeSpan.FromHours(24), "pt-BR"));
                services.AddSingleton<TimeProvider>(new FixedTimeProvider());
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
        return new AppDbContext(options,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Export Owner"
        };
        var configuration = new JwtConfiguration(
            3600, Issuer, Audience, Secret,
            new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtHandler().CreateToken(configuration));
    }

    private Dictionary<string, string?> ValidSettings() => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] =
            "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = storageRoot,
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_EXPORT_SYNC_THRESHOLD_ROWS"] = "100",
        ["FORTUNA_EXPORT_RETENTION_HOURS"] = "24",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = Secret,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10"
    };

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    }
}
