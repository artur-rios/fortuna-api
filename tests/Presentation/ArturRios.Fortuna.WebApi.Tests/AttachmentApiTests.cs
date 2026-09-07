using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Shared.Attachments;
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

public sealed class AttachmentApiTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string storageRoot = Path.Combine(
        Path.GetTempPath(),
        $"fortuna-attachment-tests-{Guid.NewGuid():N}");

    [FunctionalFact]
    public async Task GivenOwnedLiveTransaction_WhenDocumentAttached_ThenFileAndMetadataAreStored()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        (await client.GetAsync("/api/me")).EnsureSuccessStatusCode();
        var transactionId = await SeedTransactionAsync(subject);
        byte[] document = [37, 80, 68, 70, 45, 49, 46, 55];

        var response = await AttachAsync(
            client,
            transactionId,
            document,
            "invoice.pdf",
            "application/pdf");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<AttachmentEnvelope>();
        Assert.Equal(transactionId, envelope!.Data!.TransactionId);
        Assert.Equal("invoice.pdf", envelope.Data.FileName);
        Assert.Equal("application/pdf", envelope.Data.ContentType);
        Assert.Equal(document.Length, envelope.Data.SizeInBytes);
        await using var context = CreateContext();
        var attachment = await context.Attachments.SingleAsync(item =>
            item.PublicId == envelope.Data.Id);
        Assert.Equal(document, await File.ReadAllBytesAsync(Path.Combine(
            storageRoot,
            attachment.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Contains(await context.AuditEntries.ToArrayAsync(), item =>
            item.Operation == "AttachDocumentCommand" &&
            item.EntityPublicId == attachment.PublicId);
    }

    [FunctionalFact]
    public async Task GivenDisallowedOrOversizedDocument_WhenAttached_ThenBadRequestLeavesNoArtifacts()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory(maximumBytes: 3);
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        (await client.GetAsync("/api/me")).EnsureSuccessStatusCode();
        var transactionId = await SeedTransactionAsync(subject);

        var oversized = await AttachAsync(
            client, transactionId, [1, 2, 3, 4], "large.pdf", "application/pdf");
        var disallowed = await AttachAsync(
            client, transactionId, [1], "notes.txt", "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Contains("maximum of 3 bytes", await oversized.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, disallowed.StatusCode);
        Assert.Contains("application/pdf, image/png", await disallowed.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var context = CreateContext();
        Assert.Empty(await context.Attachments.Where(item =>
            item.Transaction.PublicId == transactionId).ToArrayAsync());
        Assert.Empty(Directory.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories));
    }

    [FunctionalFact]
    public async Task GivenForeignDeletedOrMissingTransaction_WhenDocumentAttached_ThenNotFoundIsIndistinguishable()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, ownerSubject, HeimdallRoles.User);
        Authorize(other, otherSubject, HeimdallRoles.User);
        (await owner.GetAsync("/api/me")).EnsureSuccessStatusCode();
        (await other.GetAsync("/api/me")).EnsureSuccessStatusCode();
        var transactionId = await SeedTransactionAsync(ownerSubject);
        var deletedId = await SeedTransactionAsync(ownerSubject, deleted: true);

        var foreign = await AttachAsync(
            other, transactionId, [1], "file.pdf", "application/pdf");
        var deleted = await AttachAsync(
            owner, deletedId, [1], "file.pdf", "application/pdf");
        var missing = await AttachAsync(
            owner, Guid.NewGuid(), [1], "file.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains(AttachmentMessages.TransactionNotFound,
            await foreign.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(AttachmentMessages.TransactionNotFound,
            await deleted.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(AttachmentMessages.TransactionNotFound,
            await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenStorageUnavailable_WhenDocumentAttached_ThenServiceUnavailableLeavesNoRow()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory(unavailableStorage: true);
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        (await client.GetAsync("/api/me")).EnsureSuccessStatusCode();
        var transactionId = await SeedTransactionAsync(subject);

        var response = await AttachAsync(
            client, transactionId, [1], "file.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var context = CreateContext();
        Assert.Empty(await context.Attachments.Where(item =>
            item.Transaction.PublicId == transactionId).ToArrayAsync());
    }

    [FunctionalFact]
    public async Task GivenAnonymousOrAdministrator_WhenDocumentAttached_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var anonymousResponse = await AttachAsync(
            anonymous, Guid.NewGuid(), [1], "file.pdf", "application/pdf");
        var administratorResponse = await AttachAsync(
            administrator, Guid.NewGuid(), [1], "file.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, administratorResponse.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenOwnedLiveAttachment_WhenDownloaded_ThenOriginalFileResponseIsReturned()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        (await client.GetAsync("/api/me")).EnsureSuccessStatusCode();
        var transactionId = await SeedTransactionAsync(subject);
        byte[] document = [37, 80, 68, 70, 45, 49, 46, 55];
        var attached = await AttachAsync(
            client, transactionId, document, "statement.pdf", "application/pdf");
        var attachmentId = (await attached.Content.ReadFromJsonAsync<AttachmentEnvelope>())!.Data!.Id;

        var response = await client.GetAsync($"/api/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("statement.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(document, await response.Content.ReadAsByteArrayAsync());
    }

    [FunctionalFact]
    public async Task GivenForeignDeletedOrMissingAttachment_WhenDownloaded_ThenNotFoundIsReturned()
    {
        var ownerSubject = Guid.NewGuid();
        var otherSubject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        Authorize(owner, ownerSubject, HeimdallRoles.User);
        Authorize(other, otherSubject, HeimdallRoles.User);
        (await owner.GetAsync("/api/me")).EnsureSuccessStatusCode();
        (await other.GetAsync("/api/me")).EnsureSuccessStatusCode();
        var transactionId = await SeedTransactionAsync(ownerSubject);
        var attached = await AttachAsync(
            owner, transactionId, [1], "file.pdf", "application/pdf");
        var attachmentId = (await attached.Content.ReadFromJsonAsync<AttachmentEnvelope>())!.Data!.Id;

        var foreign = await other.GetAsync($"/api/attachments/{attachmentId}");
        await SetAttachmentDeletedAsync(attachmentId, deleted: true);
        var deleted = await owner.GetAsync($"/api/attachments/{attachmentId}");
        var missing = await owner.GetAsync($"/api/attachments/{Guid.NewGuid()}");
        await SetAttachmentDeletedAsync(attachmentId, deleted: false);
        var restored = await owner.GetAsync($"/api/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains(AttachmentMessages.AttachmentNotFound,
            await foreign.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenMissingStoredObject_WhenDownloaded_ThenNotFoundIsAudited()
    {
        var subject = Guid.NewGuid();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, subject, HeimdallRoles.User);
        (await client.GetAsync("/api/me")).EnsureSuccessStatusCode();
        var transactionId = await SeedTransactionAsync(subject);
        var attached = await AttachAsync(
            client, transactionId, [1], "file.pdf", "application/pdf");
        var attachmentId = (await attached.Content.ReadFromJsonAsync<AttachmentEnvelope>())!.Data!.Id;
        await using (var context = CreateContext())
        {
            var attachment = await context.Attachments.SingleAsync(item =>
                item.PublicId == attachmentId);
            File.Delete(Path.Combine(
                storageRoot,
                attachment.StorageKey.Replace('/', Path.DirectorySeparatorChar)));
        }

        var response = await client.GetAsync($"/api/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(AttachmentMessages.StoredObjectNotFound,
            await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var verification = CreateContext();
        Assert.Contains(await verification.AuditEntries.ToArrayAsync(), entry =>
            entry.Operation == nameof(DownloadAttachmentQuery) &&
            entry.EntityPublicId == attachmentId &&
            entry.Reason == AttachmentMessages.StoredObjectNotFound);
    }

    [FunctionalFact]
    public async Task GivenUnreachableBacking_WhenDownloaded_ThenServiceUnavailableIsIsolated()
    {
        var subject = Guid.NewGuid();
        Guid attachmentId;
        await using (var healthyFactory = CreateFactory())
        {
            using var client = healthyFactory.CreateClient();
            Authorize(client, subject, HeimdallRoles.User);
            (await client.GetAsync("/api/me")).EnsureSuccessStatusCode();
            var transactionId = await SeedTransactionAsync(subject);
            var attached = await AttachAsync(
                client, transactionId, [1], "file.pdf", "application/pdf");
            attachmentId = (await attached.Content.ReadFromJsonAsync<AttachmentEnvelope>())!.Data!.Id;
        }

        await using var unavailableFactory = CreateFactory(unavailableStorage: true);
        using var unavailable = unavailableFactory.CreateClient();
        Authorize(unavailable, subject, HeimdallRoles.User);
        var response = await unavailable.GetAsync($"/api/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await unavailable.GetAsync("/api/me")).StatusCode);
    }

    [FunctionalFact]
    public async Task GivenAnonymousOrAdministrator_WhenAttachmentDownloaded_ThenAccessIsRefused()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);

        var anonymousResponse = await anonymous.GetAsync($"/api/attachments/{Guid.NewGuid()}");
        var administratorResponse = await administrator.GetAsync(
            $"/api/attachments/{Guid.NewGuid()}");

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

    public async Task DisposeAsync()
    {
        await database.DisposeAsync();
        if (Directory.Exists(storageRoot))
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(
        int maximumBytes = 10 * 1024 * 1024,
        bool unavailableStorage = false)
    {
        foreach (var setting in ValidSettings(maximumBytes))
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
                if (unavailableStorage)
                {
                    services.RemoveAll<IAttachmentStore>();
                    services.AddSingleton<IAttachmentStore>(new UnavailableAttachmentStore());
                }
            });
        });
    }

    private async Task<Guid> SeedTransactionAsync(Guid subject, bool deleted = false)
    {
        await using var context = CreateContext();
        var user = await context.UserProfiles.SingleAsync(item =>
            item.ExternalSubject == subject.ToString("D"));
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var account = new FinancialAccount(
            user, $"Account {Guid.NewGuid():N}", null, FinancialAccountType.Checking,
            currency, 0m, DateTimeOffset.UtcNow);
        var category = new Category(user, $"Category {Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        var transaction = new FinancialTransaction(
            user, account, category, TransactionDirection.Expense, 10m,
            DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow);
        if (deleted)
        {
            transaction.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.AddRange(account, category, transaction);
        await context.SaveChangesAsync();
        return transaction.PublicId;
    }

    private async Task SetAttachmentDeletedAsync(Guid attachmentId, bool deleted)
    {
        await using var context = CreateContext();
        var attachment = await context.Attachments.SingleAsync(item => item.PublicId == attachmentId);
        if (deleted)
        {
            attachment.SoftDelete(DateTimeOffset.UtcNow);
        }
        else
        {
            attachment.Restore(DateTimeOffset.UtcNow);
        }

        await context.SaveChangesAsync();
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

    private static async Task<HttpResponseMessage> AttachAsync(
        HttpClient client,
        Guid transactionId,
        byte[] document,
        string fileName,
        string contentType)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(document);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);
        return await client.PostAsync($"/api/transactions/{transactionId}/attachments", form);
    }

    private static void Authorize(HttpClient client, Guid subject, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(subject, (int)role, Guid.NewGuid(), [])
        {
            DisplayName = "Attachment Owner"
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

    private Dictionary<string, string?> ValidSettings(int maximumBytes) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] =
            "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = storageRoot,
        ["FORTUNA_UPLOAD_MAX_BYTES"] = maximumBytes.ToString(),
        ["FORTUNA_UPLOAD_ALLOWED_CONTENT_TYPES"] = "application/pdf,image/png",
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

    private sealed class UnavailableAttachmentStore : IAttachmentStore
    {
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task WriteAsync(string key, Stream content, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed record AttachmentEnvelope(AttachmentData? Data);
    private sealed record AttachmentData(
        Guid Id,
        Guid TransactionId,
        string FileName,
        string ContentType,
        long SizeInBytes);
}
