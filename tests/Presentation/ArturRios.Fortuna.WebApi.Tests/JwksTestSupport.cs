using System.Net;
using System.Security.Cryptography;
using System.Text;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Jwt;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>Settings, keys and key sets for the tests of Fortuna's bearer-token verification.</summary>
internal static class JwksTestSupport
{
    public const string Issuer = "heimdall-tests";
    public const string Audience = "fortuna-tests";
    public const string SharedSecret = "fortuna-tests-signing-key-with-enough-entropy";
    public const string LocalSecret = "fortuna-tests-local-token-secret-with-enough-entropy";
    public const string JwksUrl = "http://heimdall.example.test/.well-known/jwks.json";

    /// <summary>A new P-256 private key as a PKCS#8 PEM.</summary>
    public static string NewPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return key.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>A new ES256 signing key, as Heimdall holds one.</summary>
    public static JwtKey NewKey() => JwtKey.FromEcPrivateKeyPem(NewPem());

    /// <summary>The public half of <paramref name="pem" /> as a SubjectPublicKeyInfo PEM: what anyone can have.</summary>
    public static string PublicPemOf(string pem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);

        return key.ExportSubjectPublicKeyInfoPem();
    }

    /// <summary>The key set Heimdall would publish for these keys.</summary>
    public static string KeySet(params JwtKey[] keys) => JwksDocument.From(keys.Select(key => key.ToPublicKey()));

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

    /// <summary>The options those settings parse into.</summary>
    public static FortunaOptions Options(bool sharedSecret = true) =>
        FortunaOptions.From(Settings(sharedSecret).GetValueOrDefault);
}

/// <summary>Stands in for Heimdall's key-set endpoint, counting the requests it is sent.</summary>
internal sealed class StubJwksHandler : HttpMessageHandler
{
    private int _requests;

    public int Requests => Volatile.Read(ref _requests);

    public Func<CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public void Serve(string body) => Respond = _ => Task.FromResult(Json(body));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);

        return Respond(cancellationToken);
    }
}

/// <summary>An <see cref="IHttpClientFactory" /> whose clients all send through one handler.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}
