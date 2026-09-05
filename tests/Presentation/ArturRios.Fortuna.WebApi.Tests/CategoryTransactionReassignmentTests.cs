using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Auditing;
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

public sealed class CategoryTransactionReassignmentTests : IAsyncLifetime
{
    private const string Secret = "fortuna-tests-signing-key-with-enough-entropy";
    private const string Issuer = "heimdall-tests";
    private const string Audience = "fortuna-tests";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    [FunctionalFact]
    public async Task GivenDirectLiveTransactions_WhenReassigned_ThenOnlyThoseTransactionsMove()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var source = await CreateCategoryAsync(client, "Source");
        var child = await CreateCategoryAsync(client, "Child", source.Id);
        var target = await CreateCategoryAsync(client, "Target");
        var first = await AddTransactionAsync(source.Id, "Direct one");
        var second = await AddTransactionAsync(source.Id, "Direct two");
        var deleted = await AddTransactionAsync(source.Id, "Deleted", deleted: true);
        var descendant = await AddTransactionAsync(child.Id, "Descendant");

        var response = await ReassignAsync(client, source.Id, target.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, response.Data?.ReassignedCount);
        Assert.Equal(source.Id, response.Data?.Id);
        Assert.Equal(target.Id, response.Data?.TargetCategoryId);
        Assert.False(response.Data!.IncludeDescendants);
        Assert.Contains(CategoryMessages.TransactionsReassignedSuccessfully, response.Messages);
        Assert.Equal(target.Id, await TransactionCategoryAsync(first));
        Assert.Equal(target.Id, await TransactionCategoryAsync(second));
        Assert.Equal(source.Id, await TransactionCategoryAsync(deleted));
        Assert.Equal(child.Id, await TransactionCategoryAsync(descendant));
        await using var context = CreateContext();
        var audit = await context.AuditEntries.SingleAsync(item =>
            item.Operation == "ReassignCategoryTransactionsCommand");
        Assert.Equal(AuditOutcome.Succeeded, audit.Outcome);
        Assert.Equal(source.Id, audit.EntityPublicId);
    }

    [FunctionalFact]
    public async Task GivenDescendants_WhenIncluded_ThenEveryLiveSubtreeTransactionMoves()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var source = await CreateCategoryAsync(client, "Tree Source");
        var child = await CreateCategoryAsync(client, "Tree Child", source.Id);
        var grandchild = await CreateCategoryAsync(client, "Tree Grandchild", child.Id);
        var target = await CreateCategoryAsync(client, "Tree Target", child.Id);
        var sourceTransaction = await AddTransactionAsync(source.Id, "Source transaction");
        var childTransaction = await AddTransactionAsync(child.Id, "Child transaction");
        var grandchildTransaction = await AddTransactionAsync(
            grandchild.Id,
            "Grandchild transaction");
        var existingTargetTransaction = await AddTransactionAsync(
            target.Id,
            "Already target");

        var response = await ReassignAsync(
            client,
            source.Id,
            target.Id,
            includeDescendants: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, response.Data?.ReassignedCount);
        Assert.True(response.Data!.IncludeDescendants);
        Assert.Equal(target.Id, await TransactionCategoryAsync(sourceTransaction));
        Assert.Equal(target.Id, await TransactionCategoryAsync(childTransaction));
        Assert.Equal(target.Id, await TransactionCategoryAsync(grandchildTransaction));
        Assert.Equal(target.Id, await TransactionCategoryAsync(existingTargetTransaction));
    }

    [FunctionalFact]
    public async Task GivenSameCategory_WhenReassigned_ThenBadRequestIsReturned()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var category = await CreateCategoryAsync(client, "Same");

        var response = await client.PostAsJsonAsync(
            $"/api/categories/{category.Id}/reassign",
            new { TargetCategoryId = category.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            CategoryMessages.SourceAndTargetMustDiffer,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [FunctionalFact]
    public async Task GivenEmptySource_WhenReassigned_ThenZeroIsReported()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var source = await CreateCategoryAsync(client, "Empty Source");
        var target = await CreateCategoryAsync(client, "Empty Target");

        var response = await ReassignAsync(client, source.Id, target.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, response.Data?.ReassignedCount);
    }

    [FunctionalFact]
    public async Task GivenMissingDeletedOrForeignCategory_WhenReassigned_ThenNotFoundIsReturned()
    {
        await using var factory = CreateFactory();
        using var ownerClient = factory.CreateClient();
        Authorize(ownerClient, Guid.NewGuid(), HeimdallRoles.User);
        var source = await CreateCategoryAsync(ownerClient, "Visible Source");
        var target = await CreateCategoryAsync(ownerClient, "Visible Target");
        var deleted = await CreateCategoryAsync(ownerClient, "Deleted Category");
        await using (var context = CreateContext())
        {
            var category = await context.Categories.SingleAsync(item => item.PublicId == deleted.Id);
            category.SoftDelete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        using var foreignClient = factory.CreateClient();
        Authorize(foreignClient, Guid.NewGuid(), HeimdallRoles.User);
        var foreign = await CreateCategoryAsync(foreignClient, "Foreign Category");

        var missingSource = await ownerClient.PostAsJsonAsync(
            $"/api/categories/{Guid.NewGuid()}/reassign",
            new { TargetCategoryId = target.Id });
        var deletedTarget = await ownerClient.PostAsJsonAsync(
            $"/api/categories/{source.Id}/reassign",
            new { TargetCategoryId = deleted.Id });
        var foreignTarget = await ownerClient.PostAsJsonAsync(
            $"/api/categories/{source.Id}/reassign",
            new { TargetCategoryId = foreign.Id });
        var foreignSource = await foreignClient.PostAsJsonAsync(
            $"/api/categories/{source.Id}/reassign",
            new { TargetCategoryId = foreign.Id });

        Assert.Equal(HttpStatusCode.NotFound, missingSource.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedTarget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignTarget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignSource.StatusCode);
        foreach (var response in new[]
        {
            missingSource,
            deletedTarget,
            foreignTarget,
            foreignSource
        })
        {
            Assert.Contains(
                CategoryMessages.NotFound,
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
    }

    [FunctionalFact]
    public async Task GivenDatabaseFailure_WhenReassigned_ThenNoTransactionIsMoved()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var source = await CreateCategoryAsync(client, "Atomic Source");
        var target = await CreateCategoryAsync(client, "Atomic Target");
        var first = await AddTransactionAsync(source.Id, "Would move");
        var rejected = await AddTransactionAsync(source.Id, "Force reassignment failure");
        await CreateFailureTriggerAsync();
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(
                $"/api/categories/{source.Id}/reassign",
                new { TargetCategoryId = target.Id });
        }
        finally
        {
            await DropFailureTriggerAsync();
        }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(source.Id, await TransactionCategoryAsync(first));
        Assert.Equal(source.Id, await TransactionCategoryAsync(rejected));
    }

    [FunctionalFact]
    public async Task GivenInvalidBodyOrAccess_WhenReassigned_ThenRequestIsRefused()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        Authorize(client, Guid.NewGuid(), HeimdallRoles.User);
        var source = await CreateCategoryAsync(client, "Access Source");
        var invalid = await client.PostAsJsonAsync(
            $"/api/categories/{source.Id}/reassign",
            new { TargetCategoryId = Guid.Empty });
        using var anonymous = factory.CreateClient();
        using var administrator = factory.CreateClient();
        Authorize(administrator, Guid.NewGuid(), HeimdallRoles.SystemAdmin);
        var anonymousResponse = await anonymous.PostAsJsonAsync(
            $"/api/categories/{source.Id}/reassign",
            new { TargetCategoryId = Guid.NewGuid() });
        var administratorResponse = await administrator.PostAsJsonAsync(
            $"/api/categories/{source.Id}/reassign",
            new { TargetCategoryId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains(
            CategoryMessages.TargetCategoryIdInvalid,
            await invalid.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
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

    private async Task<Guid> AddTransactionAsync(
        Guid categoryId,
        string description,
        bool deleted = false)
    {
        await using var context = CreateContext();
        var category = await context.Categories
            .Include(item => item.User)
            .SingleAsync(item => item.PublicId == categoryId);
        var currency = await context.Currencies.SingleAsync(item => item.Code == "BRL");
        var account = new FinancialAccount(
            category.User,
            $"Account {Guid.NewGuid():N}",
            null,
            FinancialAccountType.Checking,
            currency,
            0,
            DateTimeOffset.UtcNow);
        var transaction = new FinancialTransaction(
            category.User,
            account,
            category,
            TransactionDirection.Expense,
            25,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTimeOffset.UtcNow,
            description);
        if (deleted)
        {
            transaction.SoftDelete(DateTimeOffset.UtcNow);
        }

        context.FinancialAccounts.Add(account);
        context.FinancialTransactions.Add(transaction);
        await context.SaveChangesAsync();
        return transaction.PublicId;
    }

    private async Task<Guid> TransactionCategoryAsync(Guid transactionId)
    {
        await using var context = CreateContext();
        return await context.FinancialTransactions
            .Where(item => item.PublicId == transactionId)
            .Select(item => item.Category.PublicId)
            .SingleAsync();
    }

    private async Task CreateFailureTriggerAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION fortuna.fail_category_reassignment()
            RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                IF OLD.description = 'Force reassignment failure'
                   AND NEW.category_id <> OLD.category_id THEN
                    RAISE EXCEPTION 'forced category reassignment failure';
                END IF;
                RETURN NEW;
            END;
            $function$;
            CREATE TRIGGER fail_category_reassignment_trigger
            BEFORE UPDATE OF category_id ON fortuna.financial_transaction
            FOR EACH ROW EXECUTE FUNCTION fortuna.fail_category_reassignment();
            """);
    }

    private async Task DropFailureTriggerAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER IF EXISTS fail_category_reassignment_trigger
                ON fortuna.financial_transaction;
            DROP FUNCTION IF EXISTS fortuna.fail_category_reassignment();
            """);
    }

    private static async Task<CategoryData> CreateCategoryAsync(
        HttpClient client,
        string name,
        Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { Name = name, ParentId = parentId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryEnvelope>())!.Data!;
    }

    private static async Task<ReassignmentEnvelope> ReassignAsync(
        HttpClient client,
        Guid sourceId,
        Guid targetId,
        bool includeDescendants = false)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/categories/{sourceId}/reassign",
            new { TargetCategoryId = targetId, IncludeDescendants = includeDescendants });
        var envelope = (await response.Content.ReadFromJsonAsync<ReassignmentEnvelope>())!;
        return envelope with { StatusCode = response.StatusCode };
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

    private sealed record CategoryEnvelope(CategoryData? Data);

    private sealed record CategoryData(Guid Id);

    private sealed record ReassignmentEnvelope(
        ReassignmentData? Data,
        IReadOnlyCollection<string> Messages)
    {
        public HttpStatusCode StatusCode { get; init; }
    }

    private sealed record ReassignmentData(
        Guid Id,
        Guid TargetCategoryId,
        bool IncludeDescendants,
        int ReassignedCount);
}
