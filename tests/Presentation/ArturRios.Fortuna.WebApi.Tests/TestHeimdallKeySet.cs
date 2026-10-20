using System.Runtime.CompilerServices;
using ArturRios.Jwt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// Heimdall as the tests see it: one ES256 key for the whole run, published by an in-process key set, and
/// the signer that makes a test's token an ES256 token under that key.
/// </summary>
/// <remarks>
/// The key set is served before any test host is built: <see cref="Start" /> runs when the test assembly is
/// first used and points <c>FORTUNA_HEIMDALL_JWKS_URL</c> at it, overriding <c>default.runsettings</c>. A test
/// host that reads its settings from the environment then fetches Heimdall's key from here, as it would from
/// Heimdall.
/// </remarks>
internal static class TestHeimdallKeySet
{
    private const string KeySetPath = "/.well-known/jwks.json";

    /// <summary>The key Heimdall signs with during this test run.</summary>
    public static JwtKey Key { get; } = JwksTestSupport.NewKey();

    /// <summary>Where the key set is published: <c>http://127.0.0.1:&lt;port&gt;/.well-known/jwks.json</c>.</summary>
    public static string Url { get; private set; } = string.Empty;

    /// <summary>
    /// A token as Heimdall issues one: <paramref name="configuration" />'s lifetime, issuer, audience and
    /// claims, signed ES256 with <see cref="Key" /> under its kid. Any secret the configuration names is ignored.
    /// </summary>
    public static string Sign(JwtConfiguration configuration) =>
        new JwtHandler().CreateToken(configuration with
        {
            Algorithm = JwtAlgorithms.ES256,
            Keys = [Key],
            SigningKeyId = Key.Id,
            Secret = string.Empty
        });

    [ModuleInitializer]
    internal static void Start()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var keySet = JwksTestSupport.KeySet(Key);

        app.Run(context =>
        {
            if (context.Request.Path != KeySetPath)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return Task.CompletedTask;
            }

            context.Response.ContentType = "application/json";

            return context.Response.WriteAsync(keySet);
        });
        app.StartAsync().GetAwaiter().GetResult();

        Url = new Uri(new Uri(app.Urls.Single()), KeySetPath).AbsoluteUri;
        Environment.SetEnvironmentVariable("FORTUNA_HEIMDALL_JWKS_URL", Url);
    }
}
