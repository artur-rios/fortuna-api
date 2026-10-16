namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>Settings, keys and key sets for the tests of Fortuna's bearer-token verification.</summary>
internal static class JwksTestSupport
{
    public const string Issuer = "heimdall-tests";
    public const string Audience = "fortuna-tests";
    public const string SharedSecret = "fortuna-tests-signing-key-with-enough-entropy";
    public const string LocalSecret = "fortuna-tests-local-token-secret-with-enough-entropy";
    public const string JwksUrl = "http://heimdall.example.test/.well-known/jwks.json";

    /// <summary>Every setting Fortuna needs to start, with or without the legacy shared secret.</summary>
    public static Dictionary<string, string?> Settings(bool sharedSecret = true) => new()
    {
        ["FORTUNA_DATA_CONNECTIONSTRING"] = "Host=localhost;Database=fortuna;Username=postgres;Password=postgres;Search Path=fortuna",
        ["FORTUNA_DATA_DATABASETYPE"] = "PostgreSql",
        ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
        ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-api-tests"),
        ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
        ["FORTUNA_JOB_QUEUE_CAPACITY"] = "32",
        ["FORTUNA_AUTH_TOKEN_SECRET"] = sharedSecret ? SharedSecret : null,
        ["FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS"] = null,
        ["FORTUNA_AUTH_TOKEN_ISSUER"] = Issuer,
        ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = Audience,
        ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
        ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
        ["FORTUNA_LOCALE"] = "pt-BR",
        ["FORTUNA_LOCAL_AUTH_ENABLED"] = "false",
        ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
        ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
        ["FORTUNA_HEIMDALL_JWKS_URL"] = JwksUrl,
        ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000076",
        ["FORTUNA_LOCAL_TOKEN_SECRET"] = LocalSecret,
        ["FORTUNA_LOCAL_TOKEN_ISSUER"] = null
    };
}
