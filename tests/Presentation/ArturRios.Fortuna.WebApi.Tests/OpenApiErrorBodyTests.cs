using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Real error responses, from a host on a SQLite database, checked against what docs/openapi/fortuna.json
///     documents for them: the status is listed, the media type the body comes as is listed with a schema that
///     accepts the body, a body the document says is absent is absent, and the headers sent are listed.
/// </summary>
public sealed class OpenApiErrorBodyTests
{
    [FunctionalFact]
    public async Task GivenRequestsThePipelineRefuses_WhenSent_ThenEachAnswerIsTheOneTheDocumentDescribes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fortuna-openapi-errors-{Guid.NewGuid():N}.db");
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["FORTUNA_DATA_CONNECTIONSTRING"] = path,
                ["FORTUNA_DATA_DATABASETYPE"] = DatabaseProvider.SQLite,
                ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
                ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-openapi-errors-files"),
                ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
                ["FORTUNA_AUTH_TOKEN_ISSUER"] = "heimdall-tests",
                ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = "fortuna-tests",
                ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
                ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
                ["FORTUNA_LOCALE"] = "pt-BR",
                ["FORTUNA_LOCAL_AUTH_ENABLED"] = "true",
                ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
                ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
                ["FORTUNA_HEIMDALL_JWKS_URL"] = TestHeimdallKeySet.Url,
                ["FORTUNA_LOCAL_TOKEN_SECRET"] = "fortuna-tests-local-token-secret-with-enough-entropy",
                ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000155"
            };
            foreach (var setting in settings)
            {
                Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            }

            var builder = new DbContextOptionsBuilder<AppDbContext>();
            DatabaseProvider.Configure(builder, DatabaseProvider.SQLite, path);
            await using (var setup = new AppDbContext(builder.Options, NullLoggerFactory.Instance,
                             DatabaseDiagnosticsOptions.Disabled))
            {
                await setup.Database.MigrateAsync();
                await new DatabaseSeeder(setup).SeedAsync(CancellationToken.None);
            }

            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Staging");
                host.ConfigureServices(services => services.RemoveAll<IHostedService>());
            });
            using var anonymous = factory.CreateClient();
            using var user = factory.CreateClient();
            Authorize(user, HeimdallRoles.User);
            using var admin = factory.CreateClient();
            Authorize(admin, HeimdallRoles.SystemAdmin);
            using var bad = factory.CreateClient();
            bad.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

            HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

            HttpRequestMessage Post(string url, string content, string type) =>
                new(HttpMethod.Post, url) { Content = new StringContent(content, Encoding.UTF8, type) };

            const string account = "/api/accounts/00000000-0000-0000-0000-000000000001";
            const string transaction = "/api/transactions/00000000-0000-0000-0000-000000000001/attachments";

            await AssertDocumented("GET /api/accounts", 401, await anonymous.SendAsync(Get("/api/accounts")));
            await AssertDocumented("GET /api/accounts", 401, await bad.SendAsync(Get("/api/accounts")));
            await AssertDocumented("GET /api/accounts", 403, await admin.SendAsync(Get("/api/accounts")));
            await AssertDocumented("GET /healthcheck/detailed", 403,
                await user.SendAsync(Get("/healthcheck/detailed")));
            await AssertDocumented("POST /api/exchange-rates/sync", 403,
                await user.SendAsync(Post("/api/exchange-rates/sync", "{}", "application/json")));
            await AssertDocumented("POST /api/accounts", 415,
                await user.SendAsync(Post("/api/accounts", "x", "text/plain")));
            await AssertDocumented("POST /api/transactions/{id}/attachments", 415,
                await user.SendAsync(Post(transaction, "{}", "application/json")));
            await AssertDocumented("POST /api/accounts", 400,
                await user.SendAsync(Post("/api/accounts", "{bad", "application/json")));
            await AssertDocumented("GET /api/accounts/{id}", 400,
                await user.SendAsync(Get($"{account}?includeDeleted=maybe")));
            await AssertDocumented("GET /api/accounts/{id}", 404, await user.SendAsync(Get(account)));

            HttpResponseMessage limited;
            var attempts = 0;
            do
            {
                limited = await anonymous.SendAsync(Post("/api/local-accounts/authenticate",
                    "{\"name\":\"nobody\",\"secret\":\"wrong\"}", "application/json"));
                if (attempts == 0)
                {
                    await AssertDocumented("POST /api/local-accounts/authenticate", 401, limited);
                }
            }
            while (limited.StatusCode != HttpStatusCode.TooManyRequests && ++attempts < 20);

            await AssertDocumented("POST /api/local-accounts/authenticate", 429, limited);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    /// <summary>Asserts that the document describes <paramref name="response" /> for the operation.</summary>
    private static async Task AssertDocumented(string operation, int status, HttpResponseMessage response)
    {
        Assert.Equal(status, (int)response.StatusCode);
        AssertDocumented(operation, status, response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync());
        var headers = CommittedOpenApiDocument.Headers(operation, status);

        if (response.Headers.WwwAuthenticate.Count > 0)
        {
            Assert.Contains("WWW-Authenticate", headers);
        }

        if (response.Headers.RetryAfter is not null)
        {
            Assert.Contains("Retry-After", headers);
        }
    }

    /// <summary>
    ///     Asserts that the document lists <paramref name="status" /> for the operation, and allows for a body of
    ///     <paramref name="mediaType" /> (none when <paramref name="body" /> is empty) that its schema accepts.
    /// </summary>
    internal static void AssertDocumented(string operation, int status, string? mediaType, string body)
    {
        var documented = CommittedOpenApiDocument.Response(operation, status);
        var content = CommittedOpenApiDocument.Content(operation, status);
        if (body.Length == 0)
        {
            // No body: the document lists no content, or says which source sends none.
            var description = documented.GetProperty("description").GetString()!;
            Assert.True(content.Count == 0 || description.Contains("No body", StringComparison.Ordinal) ||
                        description.Contains("and no body", StringComparison.Ordinal),
                $"{operation} {status} came with no body, which the document does not allow for.");
        }
        else
        {
            var schema = Assert.Single(content, media => media.Key == mediaType).Value;
            using var parsed = JsonDocument.Parse(body);

            Assert.True(CommittedOpenApiDocument.Accepts(schema, parsed.RootElement, out var failure),
                $"{operation} {status} {mediaType}: the document's schema rejects {body}: {failure}.");
        }
    }

    private static void Authorize(HttpClient client, HeimdallRoles role)
    {
        var identity = new FortunaIdentity(Guid.NewGuid(), (int)role, Guid.NewGuid(), []) { DisplayName = "Probe" };
        var configuration = new JwtConfiguration(3600, "heimdall-tests", "fortuna-tests", string.Empty,
            new FortunaIdentityMapper().ToClaims(identity));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestHeimdallKeySet.Sign(configuration));
    }
}
