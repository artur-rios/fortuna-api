using System.Globalization;

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
    public int PageSizeMaximum { get; init; }
    public bool RunMigrations { get; init; }
    public required string AuthTokenSecret { get; init; }
    public string? AuthPreviousTokenSecret { get; init; }
    public required string AuthTokenIssuer { get; init; }
    public required string AuthTokenAudience { get; init; }
    public double AuthTokenExpirationInSeconds { get; init; }
    public string? DefaultDisplayCurrency { get; init; }
    public required string Locale { get; init; }
    public bool LocalAuthEnabled { get; init; }
    public int LocalAuthRecoveryCodeCount { get; init; }
    public Uri? RatesSourceBaseUri { get; init; }
    public string? RatesSyncCron { get; init; }
    public IReadOnlyCollection<string> RatesCurrencies { get; init; } = [];
    public decimal ReconciliationAmountTolerance { get; init; }
    public int ReconciliationDateToleranceDays { get; init; }

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
            PageSizeMaximum = PositiveInteger(read("FORTUNA_PAGE_SIZE_MAX"), "FORTUNA_PAGE_SIZE_MAX", 100),
            RunMigrations = Boolean(read("FORTUNA_RUN_MIGRATIONS"), "FORTUNA_RUN_MIGRATIONS", false),
            AuthTokenSecret = Required(read, "FORTUNA_AUTH_TOKEN_SECRET"),
            AuthPreviousTokenSecret = read("FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS"),
            AuthTokenIssuer = Required(read, "FORTUNA_AUTH_TOKEN_ISSUER"),
            AuthTokenAudience = Required(read, "FORTUNA_AUTH_TOKEN_AUDIENCE"),
            AuthTokenExpirationInSeconds = PositiveDouble(
                read("FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"),
                "FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS",
                3600),
            DefaultDisplayCurrency = CurrencyCode(read("FORTUNA_DEFAULT_DISPLAY_CURRENCY")),
            Locale = SpecificLocale(read("FORTUNA_LOCALE")),
            LocalAuthEnabled = Boolean(read("FORTUNA_LOCAL_AUTH_ENABLED"), "FORTUNA_LOCAL_AUTH_ENABLED", false),
            LocalAuthRecoveryCodeCount = PositiveInteger(
                read("FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"),
                "FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT",
                10),
            RatesSourceBaseUri = OptionalAbsoluteUri(
                read("FORTUNA_RATES_SOURCE_BASE_URL"),
                "FORTUNA_RATES_SOURCE_BASE_URL"),
            RatesSyncCron = read("FORTUNA_RATES_SYNC_CRON"),
            RatesCurrencies = CurrencyCodes(read("FORTUNA_RATES_CURRENCIES")),
            ReconciliationAmountTolerance = NonNegativeDecimal(
                read("FORTUNA_RECONCILIATION_AMOUNT_TOLERANCE"),
                "FORTUNA_RECONCILIATION_AMOUNT_TOLERANCE",
                0.01m),
            ReconciliationDateToleranceDays = NonNegativeInteger(
                read("FORTUNA_RECONCILIATION_DATE_TOLERANCE_DAYS"),
                "FORTUNA_RECONCILIATION_DATE_TOLERANCE_DAYS",
                1)
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

        if (options.RatesSourceBaseUri is not null)
        {
            if (string.IsNullOrWhiteSpace(options.RatesSyncCron))
            {
                throw new InvalidOperationException(
                    "Required environment variable 'FORTUNA_RATES_SYNC_CRON' is not set.");
            }

            try
            {
                _ = Services.CronSchedule.Parse(options.RatesSyncCron);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "FORTUNA_RATES_SYNC_CRON must be a valid five-field UTC cron expression.",
                    exception);
            }

            if (options.RatesCurrencies.Count < 2)
            {
                throw new InvalidOperationException(
                    "FORTUNA_RATES_CURRENCIES must contain at least two ISO 4217 codes.");
            }
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

    private static int NonNegativeInteger(string? value, string key, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= 0
            ? parsed
            : throw new InvalidOperationException(
                $"Environment variable '{key}' must be a non-negative integer.");
    }

    private static decimal NonNegativeDecimal(string? value, string key, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed) && parsed >= 0
            ? parsed
            : throw new InvalidOperationException(
                $"Environment variable '{key}' must be a non-negative decimal.");
    }

    private static double PositiveDouble(string? value, string key, double fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return double.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"Environment variable '{key}' must be a positive number.");
    }

    private static string? CurrencyCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var code = value.Trim().ToUpperInvariant();

        return code.Length == 3 && code.All(char.IsAsciiLetter)
            ? code
            : throw new InvalidOperationException(
                "FORTUNA_DEFAULT_DISPLAY_CURRENCY must be a three-letter ISO 4217 code when set.");
    }

    private static IReadOnlyCollection<string> CurrencyCodes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => CurrencyCode(code)!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static Uri? OptionalAbsoluteUri(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"Environment variable '{key}' must be an absolute HTTP or HTTPS URL.");
        }

        return uri;
    }

    private static string SpecificLocale(string? value)
    {
        const string key = "FORTUNA_LOCALE";
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required environment variable '{key}' is not set.");
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(value.Trim());
            if (culture.IsNeutralCulture)
            {
                throw new ArgumentException("A neutral culture does not identify a region.", key);
            }

            _ = new RegionInfo(culture.Name);

            return culture.Name;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Environment variable '{key}' must be a specific locale such as 'pt-BR'.",
                exception);
        }
    }
}
