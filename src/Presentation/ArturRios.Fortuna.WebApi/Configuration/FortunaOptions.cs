using System.Globalization;
using System.Net;
using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.WebApi.Configuration;

public sealed record FortunaOptions
{
    public const int DefaultMetricsPort = 9464;

    /// <summary>HS256 needs a key of at least 256 bits.</summary>
    public const int MinimumAuthTokenSecretBytes = 32;

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
    public int HealthJobMaximumPendingSeconds { get; init; }
    public int PageSizeMaximum { get; init; }
    public int ReportMaximumRangeDays { get; init; }
    public int ReportKeyLifetimeMinutes { get; init; }
    public int ProjectionMaximumHorizonDays { get; init; }
    public int ExportSynchronousThresholdRows { get; init; }
    public int ExportRetentionHours { get; init; }
    public int TransactionMaximumTags { get; init; }
    public int ExcelImportMaximumFileBytes { get; init; }
    public int PdfInvoiceImportMaximumFileBytes { get; init; }
    public int UploadMaximumBytes { get; init; }
    public IReadOnlyCollection<string> UploadAllowedContentTypes { get; init; } = [];
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
    public required string ConsentExternalDataProcessingVersion { get; init; }
    public required Uri HeimdallBaseUri { get; init; }
    public Guid HeimdallScopeId { get; init; }
    public string? PluggyClientId { get; init; }
    public string? PluggyClientSecret { get; init; }
    public Uri? PluggyBaseUri { get; init; }
    public Uri? RatesSourceBaseUri { get; init; }
    public string? RatesSyncCron { get; init; }
    public IReadOnlyCollection<string> RatesCurrencies { get; init; } = [];
    public decimal ReconciliationAmountTolerance { get; init; }
    public int ReconciliationDateToleranceDays { get; init; }
    public int MetricsPort { get; init; } = DefaultMetricsPort;

    /// <summary>
    /// Reverse-proxy networks whose <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> headers are
    /// trusted, so rate limiting and logs see the real client instead of the proxy.
    /// </summary>
    public IReadOnlyCollection<IPNetwork> ForwardedKnownNetworks { get; init; } = [];

    /// <summary>Individual reverse-proxy addresses whose forwarded headers are trusted.</summary>
    public IReadOnlyCollection<IPAddress> ForwardedKnownProxies { get; init; } = [];

    /// <summary>
    /// The ports Kestrel serves the API on, read from <c>ASPNETCORE_HTTP_PORTS</c> and
    /// <c>ASPNETCORE_HTTPS_PORTS</c> only to check they do not collide with the metrics port.
    /// </summary>
    public IReadOnlyCollection<int> ApiPorts { get; init; } = [];

    /// <summary>
    /// Reads every setting, then validates the result, and returns all problems at once so a
    /// misconfigured deployment can be fixed in one pass.
    /// </summary>
    public static FortunaOptionsParseResult Parse(Func<string, string?> read)
    {
        var settings = new SettingsReader(read);
        var options = new FortunaOptions
        {
            DataConnectionString = settings.Text("FORTUNA_DATA_CONNECTIONSTRING"),
            DataDatabaseType = settings.Text("FORTUNA_DATA_DATABASETYPE"),
            StorageProvider = settings.Text("FORTUNA_STORAGE_PROVIDER"),
            StoragePath = settings.OptionalText("FORTUNA_STORAGE_PATH"),
            StorageS3Endpoint = settings.OptionalText("FORTUNA_STORAGE_S3_ENDPOINT"),
            StorageS3Bucket = settings.OptionalText("FORTUNA_STORAGE_S3_BUCKET"),
            StorageS3AccessKey = settings.OptionalText("FORTUNA_STORAGE_S3_ACCESS_KEY"),
            StorageS3SecretKey = settings.OptionalText("FORTUNA_STORAGE_S3_SECRET_KEY"),
            LogDirectory = settings.Text("FORTUNA_LOG_DIRECTORY"),
            JobQueueCapacity = settings.PositiveInteger("FORTUNA_JOB_QUEUE_CAPACITY", 256),
            HealthJobMaximumPendingSeconds = settings.PositiveInteger("FORTUNA_HEALTH_JOB_MAX_PENDING_SECONDS", 300),
            PageSizeMaximum = settings.PositiveInteger("FORTUNA_PAGE_SIZE_MAX", 100),
            ReportMaximumRangeDays = settings.PositiveInteger("FORTUNA_REPORT_MAX_RANGE_DAYS", 366),
            ReportKeyLifetimeMinutes = settings.PositiveInteger("FORTUNA_REPORT_KEY_TTL_MINUTES", 15),
            ProjectionMaximumHorizonDays = settings.PositiveInteger("FORTUNA_PROJECTION_MAX_HORIZON_DAYS", 366),
            ExportSynchronousThresholdRows = settings.PositiveInteger("FORTUNA_EXPORT_SYNC_THRESHOLD_ROWS", 1000),
            ExportRetentionHours = settings.PositiveInteger("FORTUNA_EXPORT_RETENTION_HOURS", 24),
            TransactionMaximumTags = settings.PositiveInteger("FORTUNA_TRANSACTION_MAX_TAGS", 50),
            ExcelImportMaximumFileBytes = settings.PositiveInteger(
                "FORTUNA_EXCEL_IMPORT_MAX_BYTES",
                10 * 1024 * 1024),
            PdfInvoiceImportMaximumFileBytes = settings.PositiveInteger(
                "FORTUNA_PDF_IMPORT_MAX_BYTES",
                20 * 1024 * 1024),
            UploadMaximumBytes = settings.PositiveInteger("FORTUNA_UPLOAD_MAX_BYTES", 10 * 1024 * 1024),
            UploadAllowedContentTypes = settings.ContentTypes("FORTUNA_UPLOAD_ALLOWED_CONTENT_TYPES"),
            RunMigrations = settings.Boolean("FORTUNA_RUN_MIGRATIONS", false),
            AuthTokenSecret = settings.Text("FORTUNA_AUTH_TOKEN_SECRET"),
            AuthPreviousTokenSecret = settings.OptionalText("FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS"),
            AuthTokenIssuer = settings.Text("FORTUNA_AUTH_TOKEN_ISSUER"),
            AuthTokenAudience = settings.Text("FORTUNA_AUTH_TOKEN_AUDIENCE"),
            AuthTokenExpirationInSeconds = settings.PositiveNumber("FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS", 3600),
            DefaultDisplayCurrency = settings.CurrencyCode("FORTUNA_DEFAULT_DISPLAY_CURRENCY"),
            Locale = settings.SpecificLocale("FORTUNA_LOCALE"),
            LocalAuthEnabled = settings.Boolean("FORTUNA_LOCAL_AUTH_ENABLED", false),
            LocalAuthRecoveryCodeCount = settings.PositiveInteger("FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT", 10),
            ConsentExternalDataProcessingVersion =
                settings.OptionalTrimmed("FORTUNA_CONSENT_EXTERNAL_PROCESSING_VERSION") ?? "1.0",
            HeimdallBaseUri = settings.RequiredHttpsUri("FORTUNA_HEIMDALL_BASE_URL"),
            HeimdallScopeId = settings.RequiredGuid("FORTUNA_HEIMDALL_SCOPE_ID"),
            PluggyClientId = settings.OptionalText("FORTUNA_PLUGGY_CLIENT_ID"),
            PluggyClientSecret = settings.OptionalText("FORTUNA_PLUGGY_CLIENT_SECRET"),
            PluggyBaseUri = settings.OptionalAbsoluteUri("FORTUNA_PLUGGY_BASE_URL"),
            RatesSourceBaseUri = settings.OptionalAbsoluteUri("FORTUNA_RATES_SOURCE_BASE_URL"),
            RatesSyncCron = settings.OptionalText("FORTUNA_RATES_SYNC_CRON"),
            RatesCurrencies = settings.CurrencyCodes("FORTUNA_RATES_CURRENCIES"),
            ReconciliationAmountTolerance = settings.NonNegativeDecimal(
                "FORTUNA_RECONCILIATION_AMOUNT_TOLERANCE",
                0.01m),
            ReconciliationDateToleranceDays = settings.NonNegativeInteger(
                "FORTUNA_RECONCILIATION_DATE_TOLERANCE_DAYS",
                1),
            MetricsPort = settings.Port("FORTUNA_METRICS_PORT", DefaultMetricsPort),
            ForwardedKnownNetworks = settings.Networks("FORTUNA_FORWARDED_KNOWN_NETWORKS"),
            ForwardedKnownProxies = settings.Addresses("FORTUNA_FORWARDED_KNOWN_PROXIES"),
            ApiPorts = settings.PortList("ASPNETCORE_HTTP_PORTS", "ASPNETCORE_HTTPS_PORTS")
        };

        var validation = new FortunaOptionsValidator().Validate(options);
        var errors = settings.Errors
            .Concat(validation.Errors.Select(failure => failure.ErrorMessage))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new FortunaOptionsParseResult(options, errors);
    }

    /// <summary>Parses and validates, throwing one exception that lists every problem.</summary>
    public static FortunaOptions From(Func<string, string?> read)
    {
        var result = Parse(read);

        return result.IsValid
            ? result.Options
            : throw new FortunaConfigurationException(result.Errors);
    }

    /// <summary>
    /// Converts raw environment values, recording a message for each one that cannot be
    /// converted and substituting the default so the remaining settings are still checked.
    /// </summary>
    private sealed class SettingsReader(Func<string, string?> read)
    {
        private readonly List<string> errors = [];

        public IReadOnlyList<string> Errors => errors;

        // Text values are kept verbatim (a secret's surrounding whitespace is part of the key);
        // structured values are trimmed before they are converted.
        public string Text(string key) => read(key) ?? string.Empty;

        public string? OptionalText(string key)
        {
            var value = read(key);

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public string? OptionalTrimmed(string key) => OptionalText(key)?.Trim();

        public int PositiveInteger(string key, int fallback) =>
            Convert(key, fallback, value =>
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                    ? parsed
                    : null,
                ConfigurationMessages.PositiveInteger(key));

        public int NonNegativeInteger(string key, int fallback) =>
            Convert(key, fallback, value =>
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
                    ? parsed
                    : null,
                ConfigurationMessages.NonNegativeInteger(key));

        public int Port(string key, int fallback) =>
            Convert(key, fallback, value =>
                int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
                parsed <= IPEndPoint.MaxPort
                    ? parsed
                    : null,
                ConfigurationMessages.Port(key));

        public double PositiveNumber(string key, double fallback) =>
            Convert(key, fallback, value =>
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                double.IsFinite(parsed) && parsed > 0
                    ? parsed
                    : null,
                ConfigurationMessages.PositiveNumber(key));

        public decimal NonNegativeDecimal(string key, decimal fallback) =>
            Convert(key, fallback, value =>
                decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) &&
                parsed >= 0
                    ? parsed
                    : null,
                ConfigurationMessages.NonNegativeDecimal(key));

        public bool Boolean(string key, bool fallback) =>
            Convert(key, fallback, value => bool.TryParse(value, out var parsed) ? parsed : null,
                ConfigurationMessages.Boolean(key));

        public string? CurrencyCode(string key)
        {
            var value = OptionalTrimmed(key);
            if (value is null)
            {
                return null;
            }

            var code = value.ToUpperInvariant();
            if (IsCurrencyCode(code))
            {
                return code;
            }

            errors.Add(ConfigurationMessages.CurrencyCode(key));

            return null;
        }

        public IReadOnlyCollection<string> CurrencyCodes(string key)
        {
            var value = OptionalTrimmed(key);
            if (value is null)
            {
                return [];
            }

            var codes = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(code => code.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (codes.All(IsCurrencyCode))
            {
                return codes;
            }

            errors.Add(ConfigurationMessages.CurrencyCodes(key));

            return [];
        }

        public IReadOnlyCollection<string> ContentTypes(string key)
        {
            var source = OptionalTrimmed(key) ?? "application/pdf,image/jpeg,image/png";
            var values = source.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(contentType => contentType.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (values.Length > 0 && values.All(contentType =>
                    contentType.Contains('/', StringComparison.Ordinal) &&
                    !contentType.Any(char.IsWhiteSpace)))
            {
                return values;
            }

            errors.Add(ConfigurationMessages.UploadContentTypesInvalid);

            return [];
        }

        public Uri? OptionalAbsoluteUri(string key)
        {
            var value = OptionalTrimmed(key);
            if (value is null)
            {
                return null;
            }

            if (Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https")
            {
                return uri;
            }

            errors.Add(ConfigurationMessages.AbsoluteUri(key));

            return null;
        }

        public Uri RequiredHttpsUri(string key)
        {
            var fallback = new Uri("https://unconfigured.invalid/");
            if (OptionalTrimmed(key) is null)
            {
                errors.Add(ConfigurationMessages.Required(key));

                return fallback;
            }

            var uri = OptionalAbsoluteUri(key);
            if (uri is null)
            {
                return fallback;
            }

            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                return uri;
            }

            errors.Add(ConfigurationMessages.HttpsUri(key));

            return fallback;
        }

        public Guid RequiredGuid(string key)
        {
            if (Guid.TryParse(read(key), out var parsed) && parsed != Guid.Empty)
            {
                return parsed;
            }

            errors.Add(ConfigurationMessages.NonEmptyGuid(key));

            return Guid.Empty;
        }

        public string SpecificLocale(string key)
        {
            var value = OptionalTrimmed(key);
            if (value is null)
            {
                errors.Add(ConfigurationMessages.Required(key));

                return string.Empty;
            }

            if (TryGetSpecificCulture(value, out var name))
            {
                return name;
            }

            errors.Add(ConfigurationMessages.SpecificLocale(key));

            return string.Empty;
        }

        public IReadOnlyCollection<IPNetwork> Networks(string key)
        {
            var parts = List(key);
            var networks = new List<IPNetwork>(parts.Length);
            foreach (var part in parts)
            {
                if (!IPNetwork.TryParse(part, out var network))
                {
                    errors.Add(ConfigurationMessages.NetworksInvalid(key));

                    return [];
                }

                networks.Add(network);
            }

            return networks;
        }

        public IReadOnlyCollection<IPAddress> Addresses(string key)
        {
            var parts = List(key);
            var addresses = new List<IPAddress>(parts.Length);
            foreach (var part in parts)
            {
                if (!IPAddress.TryParse(part, out var address))
                {
                    errors.Add(ConfigurationMessages.AddressesInvalid(key));

                    return [];
                }

                addresses.Add(address);
            }

            return addresses;
        }

        /// <summary>
        /// Reads Kestrel's own port lists leniently: they are validated by Kestrel, and are read
        /// here only to detect a collision with the metrics port.
        /// </summary>
        public IReadOnlyCollection<int> PortList(params string[] keys) => keys
            .SelectMany(List)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                ? port
                : -1)
            .Where(port => port > 0)
            .Distinct()
            .ToArray();

        private string[] List(string key) =>
            (OptionalTrimmed(key) ?? string.Empty).Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private T Convert<T>(string key, T fallback, Func<string, T?> parse, string error)
            where T : struct
        {
            var value = OptionalTrimmed(key);
            if (value is null)
            {
                return fallback;
            }

            var parsed = parse(value);
            if (parsed.HasValue)
            {
                return parsed.Value;
            }

            errors.Add(error);

            return fallback;
        }

        private static bool IsCurrencyCode(string code) => code.Length == 3 && code.All(char.IsAsciiLetter);

        private static bool TryGetSpecificCulture(string value, out string name)
        {
            name = string.Empty;
            try
            {
                var culture = CultureInfo.GetCultureInfo(value, predefinedOnly: true);
                if (culture.IsNeutralCulture || culture.Name.Length == 0)
                {
                    return false;
                }

                _ = new RegionInfo(culture.Name);
                name = culture.Name;

                return true;
            }
            catch (ArgumentException)
            {
                // CultureInfo reports an unknown name only by throwing; it is translated into a
                // configuration error here and never escapes.
                return false;
            }
        }
    }
}

public sealed record FortunaOptionsParseResult(FortunaOptions Options, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Thrown once at startup, listing every configuration problem found.</summary>
public sealed class FortunaConfigurationException(IReadOnlyList<string> errors)
    : InvalidOperationException(
        $"{ConfigurationMessages.Invalid}{Environment.NewLine}{string.Join(Environment.NewLine, errors.Select(error => $" - {error}"))}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
