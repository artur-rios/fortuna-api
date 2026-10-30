using System.Collections.Concurrent;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     An empty PostgreSQL database of a test's own, in a container the whole test run shares per image.
/// </summary>
/// <remarks>
///     Each functional test used to start a PostgreSQL container of its own (xUnit builds the class, and so its
///     <see cref="IAsyncLifetime" />, once per test): starting it was most of each test's time. A database per test
///     keeps what that gave, a test that starts from nothing and leaves nothing behind, without the start. The class
///     still migrates and seeds its database itself, as it did its container's.
///     <para>
///         Ryuk, Testcontainers' reaper, is off in CI (yggdrasil's scripts/run-check.sh), so the containers are
///         removed when the test process exits.
///     </para>
/// </remarks>
public sealed class FreshDatabase(string image)
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<PostgreSqlContainer>>> Containers = new();

    private readonly string name = "test_" + Guid.NewGuid().ToString("N");
    private string? connectionString;

    static FreshDatabase()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var container in Containers.Values)
            {
                if (container.IsValueCreated && container.Value.IsCompletedSuccessfully)
                {
                    container.Value.Result.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
        };
    }

    /// <summary>Creates the database, starting the image's shared container first if no test has yet.</summary>
    public async Task StartAsync()
    {
        var server = await ServerAsync();
        await ExecuteAsync(server, $"CREATE DATABASE \"{name}\"");
        connectionString = new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
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
        await ExecuteAsync(await ServerAsync(), $"DROP DATABASE \"{name}\" WITH (FORCE)");
    }

    private async Task<string> ServerAsync()
    {
        var container = await Containers
            .GetOrAdd(image, key => new Lazy<Task<PostgreSqlContainer>>(() => StartContainerAsync(key)))
            .Value;

        return container.GetConnectionString();
    }

    private static async Task<PostgreSqlContainer> StartContainerAsync(string image)
    {
        var container = new PostgreSqlBuilder(image).Build();
        await container.StartAsync();

        return container;
    }

    private static async Task ExecuteAsync(string server, string sql)
    {
        await using var connection = new NpgsqlConnection(server);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
