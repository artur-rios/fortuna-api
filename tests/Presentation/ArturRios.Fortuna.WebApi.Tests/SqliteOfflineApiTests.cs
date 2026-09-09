using System.Net;
using System.Net.Http.Json;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class SqliteOfflineApiTests
{
    [FunctionalFact]
    public async Task GivenOfflineConfiguration_WhenUsingLocalAccountApi_ThenDataPersistsInSqlite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fortuna-api-{Guid.NewGuid():N}.db");
        try
        {
            ConfigureEnvironment(path);
            await using (var setup = CreateContext(path))
            {
                await setup.Database.MigrateAsync();
                await new DatabaseSeeder(setup).SeedAsync(CancellationToken.None);
            }

            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
            });
            using var client = factory.CreateClient();
            var command = new CreateLocalAccountCommand
            {
                DisplayName = "Offline User",
                Secret = "correct-horse-battery-staple",
                StorageMode = LocalAccountStorageMode.InMemory
            };

            var created = await client.PostAsJsonAsync("/api/local-accounts", command);
            var duplicate = await client.PostAsJsonAsync("/api/local-accounts", command);

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            await using var verification = CreateContext(path);
            Assert.Equal(1, await verification.LocalAccounts.CountAsync());
            Assert.Equal(1, await verification.UserProfiles.CountAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AppDbContext CreateContext(string path)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, DatabaseProvider.SQLite, path);
        return new AppDbContext(
            builder.Options,
            NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
    }

    private static void ConfigureEnvironment(string path)
    {
        var settings = new Dictionary<string, string?>
        {
            ["FORTUNA_DATA_CONNECTIONSTRING"] = path,
            ["FORTUNA_DATA_DATABASETYPE"] = DatabaseProvider.SQLite,
            ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
            ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-sqlite-files"),
            ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
            ["FORTUNA_AUTH_TOKEN_SECRET"] = "fortuna-tests-signing-key-with-enough-entropy",
            ["FORTUNA_AUTH_TOKEN_ISSUER"] = "heimdall-tests",
            ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = "fortuna-tests",
            ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
            ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
            ["FORTUNA_LOCALE"] = "pt-BR",
            ["FORTUNA_LOCAL_AUTH_ENABLED"] = "true",
            ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
            ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
            ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000155"
        };

        foreach (var setting in settings)
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }
    }
}
