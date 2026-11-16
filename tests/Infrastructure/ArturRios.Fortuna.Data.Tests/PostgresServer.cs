using ArturRios.Fortuna.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.Data.Tests;

/// <summary>
///     One PostgreSQL container for the whole assembly, holding a migrated template database that every PostgreSQL
///     test copies into a database of its own.
/// </summary>
/// <remarks>
///     The PostgreSQL tests used to start a container each (a test class is built once per test, and so was its
///     container) and migrate it from nothing. A copy of the template starts from the same migrated, empty schema
///     without either, and classes still share no rows, so xUnit runs them side by side. Durability is off
///     (<c>fsync</c>, <c>synchronous_commit</c>, <c>full_page_writes</c>): nothing here outlives the container. The
///     image is the major version production runs.
///     <para>
///         The server starts when the first PostgreSQL test asks for a database, not with the assembly, so a run of
///         the unit tests alone needs no Docker. It is removed when the test process exits (Ryuk, Testcontainers'
///         reaper, is off in CI).
///     </para>
/// </remarks>
public sealed class PostgresServer
{
    private const string Template = "fortuna_migrated";

    private static readonly Lazy<Task<PostgresServer>> Shared = new(StartAsync);

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off")
        .Build();

    // A database can only be copied while nobody is connected to it, so copies are made one at a time.
    private readonly SemaphoreSlim copying = new(1, 1);

    static PostgresServer()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Shared.IsValueCreated && Shared.Value.IsCompletedSuccessfully)
            {
                Shared.Value.Result.container.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        };
    }

    private PostgresServer()
    {
    }

    /// <summary>The run's server, started and its template migrated by the first caller.</summary>
    public static Task<PostgresServer> GetAsync() => Shared.Value;

    private static async Task<PostgresServer> StartAsync()
    {
        var server = new PostgresServer();
        await server.InitializeAsync();

        return server;
    }

    private async Task InitializeAsync()
    {
        await container.StartAsync();
        await ExecuteAsync($"CREATE DATABASE \"{Template}\"");

        // Migrated as the application migrates (its history table included), over an unpooled connection so none
        // stays open to the template once it is done.
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, DatabaseProvider.PostgreSql, ConnectionString(Template, pooling: false));
        await using var context = new AppDbContext(
            builder.Options,
            NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
        await context.Database.MigrateAsync();
    }

    /// <summary>Creates a copy of the migrated template and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = "test_" + Guid.NewGuid().ToString("N");
        await copying.WaitAsync();
        try
        {
            await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{Template}\"");
        }
        finally
        {
            copying.Release();
        }

        return ConnectionString(name, pooling: true);
    }

    /// <summary>Drops a database <see cref="CreateDatabaseAsync" /> made, closing whatever connections remain.</summary>
    public async Task DropDatabaseAsync(string connectionString)
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }

    private string ConnectionString(string database, bool pooling) =>
        new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = database,
            Pooling = pooling
        }.ConnectionString;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>A copy of the migrated template for one test or one fixture, dropped when it is disposed.</summary>
internal sealed class PostgresTestDatabase
{
    private string? connectionString;

    public async Task StartAsync() =>
        connectionString = await (await PostgresServer.GetAsync()).CreateDatabaseAsync();

    public string GetConnectionString() =>
        connectionString ?? throw new InvalidOperationException("The database is created by StartAsync.");

    public async ValueTask DisposeAsync()
    {
        if (connectionString is not null)
        {
            await (await PostgresServer.GetAsync()).DropDatabaseAsync(connectionString);
        }
    }
}
