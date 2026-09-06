using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
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

public sealed class ImportJobMonitoringTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 21, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("fortuna")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    [FunctionalFact]
    public async Task GivenOwnedJobs_WhenMonitored_ThenStatesCountsAndRowsAreReturned()
    {
        var subject = Guid.NewGuid();
        var seeded = await SeedJobsAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var listResponse = await client.GetAsync(
            "/api/import-jobs?Status=Completed&SortBy=UpdatedAt&Descending=true");
        var list = await listResponse.Content.ReadFromJsonAsync<JobListEnvelope>();
        var runningResponse = await client.GetAsync($"/api/import-jobs/{seeded.RunningId}");
        var running = await runningResponse.Content.ReadFromJsonAsync<JobEnvelope>();
        var failedResponse = await client.GetAsync($"/api/import-jobs/{seeded.FailedId}");
        var failed = await failedResponse.Content.ReadFromJsonAsync<JobEnvelope>();
        var recordsResponse = await client.GetAsync(
            $"/api/import-jobs/{seeded.CompletedId}/records");
        var records = await recordsResponse.Content.ReadFromJsonAsync<RecordListEnvelope>();

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(1, list?.TotalItems);
        var completed = Assert.Single(list!.Data!);
        Assert.Equal(seeded.CompletedId, completed.Id);
        Assert.Equal(3, completed.ProcessedCount);
        Assert.Equal((1, 1, 1),
            (completed.ImportedCount, completed.DuplicateCount, completed.RejectedCount));

        Assert.Equal(HttpStatusCode.OK, runningResponse.StatusCode);
        Assert.Equal(ImportJobStatus.Running, running?.Data?.Status);
        Assert.Equal(0, running?.Data?.ProcessedCount);
        Assert.Equal(HttpStatusCode.OK, failedResponse.StatusCode);
        Assert.Equal(ImportJobStatus.Failed, failed?.Data?.Status);
        Assert.Equal(PdfInvoiceImportMessages.UnsupportedLayout, failed?.Data?.FailureReason);

        Assert.Equal(HttpStatusCode.OK, recordsResponse.StatusCode);
        Assert.Equal(3, records?.TotalItems);
        Assert.Contains(records!.Data!, record =>
            record.Outcome == ImportedRecordOutcome.Imported &&
            JsonDocument.Parse(record.RawPayload).RootElement.GetProperty("row").GetInt32() == 1);
        Assert.Contains(records.Data!, record =>
            record.Outcome == ImportedRecordOutcome.Duplicate);
        Assert.Contains(records.Data!, record =>
            record.Outcome == ImportedRecordOutcome.Rejected &&
            record.RejectionReason == ExcelImportMessages.RowDateInvalid);
    }

    [FunctionalFact]
    public async Task GivenForeignOrMissingJob_WhenMonitored_ThenResponsesAreIndistinguishable()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        var owned = await SeedJobsAsync(ownerSubject);
        await SeedJobsAsync(otherSubject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, otherSubject, HeimdallRoles.User);
        var missingId = Guid.NewGuid();

        var foreign = await client.GetAsync($"/api/import-jobs/{owned.CompletedId}");
        var missing = await client.GetAsync($"/api/import-jobs/{missingId}");
        var foreignRows = await client.GetAsync(
            $"/api/import-jobs/{owned.CompletedId}/records");
        var missingRows = await client.GetAsync($"/api/import-jobs/{missingId}/records");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            (await missing.Content.ReadFromJsonAsync<ErrorEnvelope>())?.Errors,
            (await foreign.Content.ReadFromJsonAsync<ErrorEnvelope>())?.Errors);
        Assert.Equal(HttpStatusCode.NotFound, foreignRows.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingRows.StatusCode);
        Assert.Equal(
            (await missingRows.Content.ReadFromJsonAsync<ErrorEnvelope>())?.Errors,
            (await foreignRows.Content.ReadFromJsonAsync<ErrorEnvelope>())?.Errors);
    }

    [FunctionalFact]
    public async Task GivenInvalidOrUnsupportedListCriteria_WhenListed_ThenBadRequestIsReturned()
    {
        var subject = Guid.NewGuid();
        await SeedJobsAsync(subject);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);

        var invalid = await client.GetAsync("/api/import-jobs?PageNumber=0&SortBy=Name");
        var unsupported = await client.GetAsync("/api/import-jobs?FailureReason=anything");
        var unsupportedRows = await client.GetAsync(
            $"/api/import-jobs/{Guid.NewGuid()}/records?Outcome=Rejected");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(ImportJobMessages.InvalidPageNumber,
            await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(ImportJobMessages.SortByUnsupported,
            await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Contains(ImportJobMessages.UnsupportedFilter("FailureReason"),
            await unsupported.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedRows.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenUnauthorizedActor_WhenMonitoringJobs_ThenAccessIsDenied()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var unauthorized = await anonymous.GetAsync("/api/import-jobs");
        var forbidden = await administrator.GetAsync("/api/import-jobs");

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
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString()));
            });
        });
    }

    private async Task<SeededJobs> SeedJobsAsync(Guid subject)
    {
        await using var context = CreateContext();
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var user = new UserProfile(subject, $"Owner {subject:N}", currency, Now);
        var pending = new ImportJob(user, TransactionSourceType.Excel, Now.AddMinutes(-4));
        var running = new ImportJob(user, TransactionSourceType.Pluggy, Now.AddMinutes(-3));
        running.Start(Now.AddMinutes(-2));
        var completed = new ImportJob(user, TransactionSourceType.Excel, Now.AddMinutes(-2));
        completed.Start(Now.AddMinutes(-1));
        completed.SetPeriod(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            Now.AddMinutes(-1));
        var imported = Record(completed, "{\"row\":1}", ImportedRecordOutcome.Imported);
        var duplicate = Record(completed, "{\"row\":2}", ImportedRecordOutcome.Duplicate);
        var rejected = Record(
            completed,
            "{\"row\":3}",
            ImportedRecordOutcome.Rejected,
            ExcelImportMessages.RowDateInvalid);
        completed.Complete(1, 1, 1, Now);
        var failed = new ImportJob(user, TransactionSourceType.Pdf, Now.AddMinutes(-1));
        failed.Start(Now.AddMinutes(-1));
        failed.Fail(PdfInvoiceImportMessages.UnsupportedLayout, Now);
        context.AddRange(user, pending, running, completed, failed, imported, duplicate, rejected);
        await context.SaveChangesAsync();
        return new SeededJobs(pending.PublicId, running.PublicId, completed.PublicId, failed.PublicId);
    }

    private static ImportedRecord Record(
        ImportJob job,
        string payload,
        ImportedRecordOutcome outcome,
        string? reason = null) => new(
        job,
        payload,
        outcome,
        10m,
        new DateOnly(2026, 8, 1),
        rejectionReason: reason);

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

    private sealed record SeededJobs(
        Guid PendingId,
        Guid RunningId,
        Guid CompletedId,
        Guid FailedId);

    private sealed record JobListEnvelope(IReadOnlyList<JobData>? Data, int TotalItems);
    private sealed record JobEnvelope(JobData? Data);
    private sealed record JobData(
        Guid Id,
        TransactionSourceType SourceType,
        ImportJobStatus Status,
        int ProcessedCount,
        int ImportedCount,
        int DuplicateCount,
        int RejectedCount,
        string? FailureReason);
    private sealed record RecordListEnvelope(IReadOnlyList<RecordData>? Data, int TotalItems);
    private sealed record RecordData(
        string RawPayload,
        ImportedRecordOutcome Outcome,
        string? RejectionReason);
    private sealed record ErrorEnvelope(IReadOnlyList<string> Errors);
}
