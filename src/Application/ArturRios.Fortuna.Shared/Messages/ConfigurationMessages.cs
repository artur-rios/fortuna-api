namespace ArturRios.Fortuna.Shared.Messages;

/// <summary>Startup configuration errors; each names the environment variable to fix.</summary>
public static class ConfigurationMessages
{
    public const string Invalid = "The Fortuna configuration is invalid.";
    public const string DatabaseTypeUnsupported = "FORTUNA_DATA_DATABASETYPE must be 'PostgreSql' or 'SQLite'.";
    public const string StorageProviderUnsupported = "FORTUNA_STORAGE_PROVIDER must be 'Filesystem' or 'S3'.";
    public const string ConsentVersionTooLong =
        "FORTUNA_CONSENT_EXTERNAL_PROCESSING_VERSION cannot exceed 50 characters.";
    public const string UploadContentTypesInvalid =
        "FORTUNA_UPLOAD_ALLOWED_CONTENT_TYPES must be a comma-separated list of MIME types.";
    public const string RatesCurrenciesTooFew =
        "FORTUNA_RATES_CURRENCIES must contain at least two ISO 4217 codes.";

    public static string Required(string key) => $"Required environment variable '{key}' is not set.";

    public static string PositiveInteger(string key) => $"Environment variable '{key}' must be a positive integer.";

    public static string NonNegativeInteger(string key) =>
        $"Environment variable '{key}' must be a non-negative integer.";

    public static string PositiveNumber(string key) => $"Environment variable '{key}' must be a positive number.";

    public static string NonNegativeDecimal(string key) =>
        $"Environment variable '{key}' must be a non-negative decimal.";

    public static string Boolean(string key) => $"Environment variable '{key}' must be true or false.";

    public static string Port(string key) =>
        $"Environment variable '{key}' must be a TCP port between 0 and 65535.";

    public static string CurrencyCode(string key) =>
        $"{key} must be a three-letter ISO 4217 code when set.";

    public static string CurrencyCodes(string key) =>
        $"{key} must be a comma-separated list of three-letter ISO 4217 codes.";

    public static string AbsoluteUri(string key) =>
        $"Environment variable '{key}' must be an absolute HTTP or HTTPS URL.";

    public static string HttpsUri(string key) => $"Environment variable '{key}' must be an absolute HTTPS URL.";

    public static string NonEmptyGuid(string key) => $"Required environment variable '{key}' must be a non-empty GUID.";

    public static string SpecificLocale(string key) =>
        $"Environment variable '{key}' must be a specific locale such as 'pt-BR'.";

    public static string SecretTooShort(string key, int minimumBytes) =>
        $"Environment variable '{key}' must be at least {minimumBytes} bytes long.";

    public static string CronInvalid(string key, string reason) =>
        $"{key} must be a valid five-field UTC cron expression: {reason}";

    public static string NetworksInvalid(string key) =>
        $"Environment variable '{key}' must be a comma-separated list of CIDR networks such as '10.0.0.0/8'.";

    public static string AddressesInvalid(string key) =>
        $"Environment variable '{key}' must be a comma-separated list of IP addresses.";

    public static string MetricsPortCollides(string metricsKey, int port) =>
        $"{metricsKey} ({port}) must differ from the API port; otherwise every API request is answered 404.";
}
