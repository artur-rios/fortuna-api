using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Requests Kestrel refuses, sent to a real Kestrel (the in-memory test server parses no request line and
///     enforces no limit) on a SQLite database: each answer is one docs/openapi/fortuna.json lists, with the
///     body it allows for.
/// </summary>
/// <remarks>
///     The limits are lowered (a kilobyte of body, two seconds for the headers) so the test sends little and
///     waits little; what is checked is the path a refusal takes, which is the same at any limit. The request
///     line and header refusals are sent over a socket, since an HTTP client will not send them.
/// </remarks>
public sealed class KestrelRefusalTests
{
    private const string Currencies = "GET /api/currencies";
    private const string Accounts = "POST /api/accounts";
    private const string Attachments = "POST /api/transactions/{id}/attachments";
    private const string AttachmentsPath = "/api/transactions/00000000-0000-0000-0000-000000000001/attachments";

    [FunctionalFact]
    public async Task GivenRequestsKestrelRefuses_WhenSent_ThenEachAnswerIsOneTheDocumentDescribes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fortuna-kestrel-{Guid.NewGuid():N}.db");
        try
        {
            await PrepareAsync(path);
            await using var factory = new KestrelHost();
            factory.UseKestrel(0);
            factory.UseKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 1024;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
            });
            using var client = factory.CreateClient();
            var token = Token();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var port = client.BaseAddress!.Port;
            var authorization = $"Authorization: Bearer {token}\r\n";

            // The request line and headers: Kestrel's own answers, before the API sees the request.
            await AssertRawAsync(port, Currencies, 414,
                $"GET /api/currencies?x={new string('a', 9000)} HTTP/1.1\r\nHost: localhost\r\n\r\n");
            await AssertRawAsync(port, Currencies, 431,
                $"GET /api/currencies HTTP/1.1\r\nHost: localhost\r\nX-Big: {new string('a', 40000)}\r\n\r\n");
            await AssertRawAsync(port, Currencies, 505, "GET /api/currencies HTTP/3.0\r\nHost: localhost\r\n\r\n");
            await AssertRawAsync(port, Currencies, 400, "GET /api/currencies HTTP/1.1\r\n\r\n");
            await AssertRawAsync(port, Currencies, 408, "GET /api/currencies HTTP/1.1\r\nHost: localhost\r\n",
                wait: TimeSpan.FromSeconds(4));

            // A JSON body Kestrel refuses as it is read: the exception handler's 500.
            await AssertRawAsync(port, Accounts, 500,
                "POST /api/accounts HTTP/1.1\r\nHost: localhost\r\n" + authorization +
                "Content-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\nzz\r\n{}\r\n0\r\n\r\n");
            using var oversized = new HttpRequestMessage(HttpMethod.Post, "/api/accounts")
            {
                Content = new StringContent("{\"name\":\"" + new string('a', 4096) + "\"}", Encoding.UTF8,
                    "application/json")
            };
            oversized.Headers.ExpectContinue = true;
            await AssertHttpAsync(client, Accounts, 500, oversized);

            // A form over the limit, sent without a length: model binding's 400.
            using var form = new MultipartFormDataContent();
            var file = new StreamContent(new UnseekableStream(new byte[200_000]));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "File", "large.pdf");
            using var upload = new HttpRequestMessage(HttpMethod.Post, AttachmentsPath) { Content = form };
            await AssertHttpAsync(client, Attachments, 400, upload);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static async Task AssertHttpAsync(
        HttpClient client,
        string operation,
        int status,
        HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True((int)response.StatusCode == status,
            $"{operation}: expected {status}, got {(int)response.StatusCode}: {body}");
        OpenApiErrorBodyTests.AssertDocumented(
            operation, status, response.Content.Headers.ContentType?.MediaType, body);
    }

    private static async Task AssertRawAsync(
        int port,
        string operation,
        int status,
        string request,
        TimeSpan? wait = null)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = new NetworkStream(socket);
        var reading = ReadAllAsync(stream);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        if (wait is { } delay)
        {
            await Task.Delay(delay);
        }

        var answer = await reading;
        var head = answer.Split("\r\n\r\n", 2);
        var lines = head[0].Split("\r\n");
        var headers = lines.Skip(1).Select(line => line.Split(':', 2))
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        var body = head.Length > 1 ? head[1] : string.Empty;
        if (headers.TryGetValue("Transfer-Encoding", out var coding) && coding == "chunked")
        {
            // One chunk, then the last: the exception handler's envelope.
            var chunk = body.Split("\r\n", 3);
            body = chunk.Length > 1 ? chunk[1] : string.Empty;
        }

        Assert.True(lines[0].StartsWith($"HTTP/1.1 {status} ", StringComparison.Ordinal),
            $"{operation}: expected {status}, got {lines[0]}");
        var mediaType = headers.TryGetValue("Content-Type", out var type) ? type.Split(';')[0] : null;
        OpenApiErrorBodyTests.AssertDocumented(operation, status, mediaType, body);
    }

    private static async Task<string> ReadAllAsync(NetworkStream stream)
    {
        var buffer = new byte[16384];
        var read = new StringBuilder();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                read.Append(Encoding.ASCII.GetString(buffer, 0, count));
                if (read.ToString().Contains("\r\n0\r\n\r\n", StringComparison.Ordinal) ||
                    read.ToString().Contains("Content-Length: 0\r\n", StringComparison.Ordinal) &&
                    read.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            // The connection closed: what was read is the answer.
        }

        return read.ToString();
    }

    private static async Task PrepareAsync(string path)
    {
        var settings = new Dictionary<string, string?>
        {
            ["FORTUNA_DATA_CONNECTIONSTRING"] = path,
            ["FORTUNA_DATA_DATABASETYPE"] = DatabaseProvider.SQLite,
            ["FORTUNA_STORAGE_PROVIDER"] = "Filesystem",
            ["FORTUNA_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "fortuna-kestrel-files"),
            ["FORTUNA_LOG_DIRECTORY"] = Path.Combine(Path.GetTempPath(), "fortuna-api-test-logs"),
            ["FORTUNA_AUTH_TOKEN_ISSUER"] = "heimdall-tests",
            ["FORTUNA_AUTH_TOKEN_AUDIENCE"] = "fortuna-tests",
            ["FORTUNA_AUTH_TOKEN_EXPIRATION_IN_SECONDS"] = "3600",
            ["FORTUNA_DEFAULT_DISPLAY_CURRENCY"] = "BRL",
            ["FORTUNA_LOCALE"] = "pt-BR",
            ["FORTUNA_LOCAL_AUTH_ENABLED"] = "true",
            ["FORTUNA_LOCAL_AUTH_RECOVERY_CODE_COUNT"] = "10",
            ["FORTUNA_HEIMDALL_BASE_URL"] = "https://heimdall.example.test",
            ["FORTUNA_HEIMDALL_JWKS_URL"] = TestHeimdallKeySet.Url,
            ["FORTUNA_LOCAL_TOKEN_SECRET"] = "fortuna-tests-local-token-secret-with-enough-entropy",
            ["FORTUNA_HEIMDALL_SCOPE_ID"] = "00000000-0000-0000-0000-000000000155",
            ["FORTUNA_UPLOAD_MAX_BYTES"] = "1024"
        };
        foreach (var setting in settings)
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(builder, DatabaseProvider.SQLite, path);
        await using var setup = new AppDbContext(builder.Options, NullLoggerFactory.Instance,
            DatabaseDiagnosticsOptions.Disabled);
        await setup.Database.MigrateAsync();
        await new DatabaseSeeder(setup).SeedAsync(CancellationToken.None);
    }

    private static string Token()
    {
        var identity = new FortunaIdentity(Guid.NewGuid(), (int)HeimdallRoles.User, Guid.NewGuid(), [])
        {
            DisplayName = "Kestrel"
        };
        var configuration = new JwtConfiguration(3600, "heimdall-tests", "fortuna-tests", string.Empty,
            new FortunaIdentityMapper().ToClaims(identity));

        return TestHeimdallKeySet.Sign(configuration);
    }

    // Its own factory rather than WithWebHostBuilder's derived one, so UseKestrel configures the very factory
    // that builds the host and the client.
    private sealed class KestrelHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }

    // A body whose length the client cannot know, so it is sent chunked.
    private sealed class UnseekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
