namespace ArturRios.Fortuna.WebApi.Configuration;

public sealed record FortunaOptions
{
    public required string DataConnectionString { get; init; }
    public required string DataDatabaseType { get; init; }
    public required string StorageProvider { get; init; }
    public string? StoragePath { get; init; }
    public string? StorageS3Endpoint { get; init; }
    public string? StorageS3Bucket { get; init; }
    public string? StorageS3AccessKey { get; init; }
    public string? StorageS3SecretKey { get; init; }
    public required string LogDirectory { get; init; }
    public int JobQueueCapacity { get; init; }
    public bool RunMigrations { get; init; }

    public static FortunaOptions From(Func<string, string?> read)
    {
        var provider = Required(read, "FORTUNA_STORAGE_PROVIDER");
        var options = new FortunaOptions
        {
            DataConnectionString = Required(read, "FORTUNA_DATA_CONNECTIONSTRING"),
            DataDatabaseType = Required(read, "FORTUNA_DATA_DATABASETYPE"),
            StorageProvider = provider,
            StoragePath = read("FORTUNA_STORAGE_PATH"),
            StorageS3Endpoint = read("FORTUNA_STORAGE_S3_ENDPOINT"),
            StorageS3Bucket = read("FORTUNA_STORAGE_S3_BUCKET"),
            StorageS3AccessKey = read("FORTUNA_STORAGE_S3_ACCESS_KEY"),
            StorageS3SecretKey = read("FORTUNA_STORAGE_S3_SECRET_KEY"),
            LogDirectory = Required(read, "FORTUNA_LOG_DIRECTORY"),
            JobQueueCapacity = PositiveInteger(read("FORTUNA_JOB_QUEUE_CAPACITY"), "FORTUNA_JOB_QUEUE_CAPACITY", 256),
            RunMigrations = Boolean(read("FORTUNA_RUN_MIGRATIONS"), "FORTUNA_RUN_MIGRATIONS", false)
        };

        if (!string.Equals(options.DataDatabaseType, "PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("FORTUNA_DATA_DATABASETYPE must be 'PostgreSql'.");
        }

        if (string.Equals(provider, "Filesystem", StringComparison.OrdinalIgnoreCase))
        {
            _ = Required(read, "FORTUNA_STORAGE_PATH");
        }
        else if (string.Equals(provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            _ = Required(read, "FORTUNA_STORAGE_S3_ENDPOINT");
            _ = Required(read, "FORTUNA_STORAGE_S3_BUCKET");
            _ = Required(read, "FORTUNA_STORAGE_S3_ACCESS_KEY");
            _ = Required(read, "FORTUNA_STORAGE_S3_SECRET_KEY");
        }
        else
        {
            throw new InvalidOperationException("FORTUNA_STORAGE_PROVIDER must be 'Filesystem' or 'S3'.");
        }

        return options;
    }

    private static string Required(Func<string, string?> read, string key) =>
        string.IsNullOrWhiteSpace(read(key))
            ? throw new InvalidOperationException($"Required environment variable '{key}' is not set.")
            : read(key)!;

    private static int PositiveInteger(string? value, string key, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"Environment variable '{key}' must be a positive integer.");
    }

    private static bool Boolean(string? value, string key, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Environment variable '{key}' must be true or false.");
    }
}
