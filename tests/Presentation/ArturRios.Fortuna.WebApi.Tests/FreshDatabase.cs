using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     A migrated PostgreSQL database of a test's own, copied from a template in a container the whole test run shares.
/// </summary>
/// <remarks>
///     Each functional test used to start a PostgreSQL container of its own (xUnit builds the class, and so its
///     <see cref="IAsyncLifetime" />, once per test), and later migrated an empty database of its own: both were most
///     of each test's time. The run now migrates two template databases once, one seeded with the reference currencies
///     and one not, and every test gets a copy of one of them. A test still starts from a database nobody else has
///     touched and leaves nothing behind.
///     <para>
///         Durability is off in the shared server (<c>fsync</c>, <c>synchronous_commit</c>, <c>full_page_writes</c>):
///         nothing in it outlives the container. The image is the major version production runs.
///     </para>
///     <para>
///         Ryuk, Testcontainers' reaper, is off in CI (yggdrasil's scripts/run-check.sh), so the container is removed
///         when the test process exits.
///     </para>
/// </remarks>
public sealed class FreshDatabase
{
    private const string Image = "postgres:18-alpine";
    private const string MigratedTemplate = "fortuna_migrated";
    private const string SeededTemplate = "fortuna_seeded";

    private static readonly Lazy<Task<PostgreSqlContainer>> Server = new(StartServerAsync);

    private readonly string name = "test_" + Guid.NewGuid().ToString("N");
    private string? connectionString;

    static FreshDatabase()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Server.IsValueCreated && Server.Value.IsCompletedSuccessfully)
            {
                Server.Value.Result.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        };
    }

    /// <summary>
    ///     Creates the database as a copy of the migrated template, with the reference currencies seeded unless
    ///     <paramref name="seeded" /> is false. The first call of the run starts the server and builds the templates.
    /// </summary>
    public async Task StartAsync(bool seeded = true)
    {
        var server = await ServerConnectionStringAsync();
        await ExecuteAsync(server, $"CREATE DATABASE \"{name}\" TEMPLATE \"{(seeded ? SeededTemplate : MigratedTemplate)}\"");
        connectionString = Connection(server, name, pooling: true);
    }

    public string GetConnectionString() =>
        connectionString ?? throw new InvalidOperationException("The database is created by StartAsync.");

    /// <summary>Drops the database, closing whatever connections the test left open to it.</summary>
    public async Task DisposeAsync()
    {
        if (connectionString is null)
        {
            return;
        }

        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        await ExecuteAsync(await ServerConnectionStringAsync(), $"DROP DATABASE \"{name}\" WITH (FORCE)");
    }

    private static async Task<string> ServerConnectionStringAsync() => (await Server.Value).GetConnectionString();

    private static async Task<PostgreSqlContainer> StartServerAsync()
    {
        var container = new PostgreSqlBuilder(Image)
            .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off")
            .Build();
        await container.StartAsync();
        var server = container.GetConnectionString();

        // A database can only be copied while nobody is connected to it, so the templates are built over unpooled
        // connections: none stays open once the migration and the seed are done.
        await ExecuteAsync(server, $"CREATE DATABASE \"{MigratedTemplate}\"");
        await using (var context = CreateContext(Connection(server, MigratedTemplate, pooling: false)))
        {
            await context.Database.MigrateAsync();
        }

        await ExecuteAsync(server, $"CREATE DATABASE \"{SeededTemplate}\" TEMPLATE \"{MigratedTemplate}\"");
        await using (var context = CreateContext(Connection(server, SeededTemplate, pooling: false)))
        {
            await new DatabaseSeeder(context).SeedAsync(CancellationToken.None);
        }

        return container;
    }

    private static string Connection(string server, string database, bool pooling) =>
        new NpgsqlConnectionStringBuilder(server) { Database = database, Pooling = pooling }.ConnectionString;

    private static AppDbContext CreateContext(string connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options,
        NullLoggerFactory.Instance,
        DatabaseDiagnosticsOptions.Disabled);

    private static async Task ExecuteAsync(string server, string sql)
    {
        await using var connection = new NpgsqlConnection(server);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
