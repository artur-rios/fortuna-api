using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Jwt;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
/// The bearer scheme end to end, through the running app and a stub of Heimdall's key set: Heimdall's ES256
/// tokens verified through its published keys, its retired HS256 tokens refused, Fortuna's local tokens under
/// their own issuer and key, and every mix of issuer, key and algorithm refused.
/// </summary>
/// <remarks>
/// The app reads its settings from the process environment when it starts. Each test sets them and puts
/// back what was there before (<see cref="Dispose" />), so other test classes see the environment unchanged.
/// </remarks>
public sealed class TokenSchemesTests : IDisposable
{
    private const string ActorRoute = "/__tests/token-schemes/actor";
    private const string LocalIssuer = FortunaOptions.DefaultLocalTokenIssuer;
    private const string CallerKeySetting = "FORTUNA_HEIMDALL_CALLER_KEY";
    private const string SharedSecretSetting = "FORTUNA_AUTH_TOKEN_SECRET";

    /// <summary>
    /// A local secret long enough for HS384 and HS512, so that IdentityModel's key-size check cannot be what
    /// refuses them: only the one-algorithm-per-key pin can.
    /// </summary>
    private const string LongLocalSecret =
        "fortuna-tests-local-key-long-enough-for-hs384-and-hs512-0123456789abcdef";

    /// <summary>No database answers here: nothing on these routes may reach one.</summary>
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=fortuna;Username=fortuna-tests;Password=unused;Search Path=fortuna";

    private static readonly string HeimdallPem = JwksTestSupport.NewPem();
    private static readonly JwtKey HeimdallKey = JwtKey.FromEcPrivateKeyPem(HeimdallPem);

    private readonly Dictionary<string, string?> _environmentBefore = JwksTestSupport.Settings().Keys
        .Append(CallerKeySetting)
        .ToDictionary(key => key, Environment.GetEnvironmentVariable);

    public sealed record ActorProbe(Guid SubjectId, bool IsLocal);

    public void Dispose()
    {
        foreach (var (key, value) in _environmentBefore)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    [FunctionalFact]
    public async Task GivenAnEs256HeimdallToken_WhenItsKeyIsPublished_ThenTheActorIsAuthenticated()
    {
        var jwks = new StubJwksHandler();
        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));
        await using var factory = CreateFactory(jwks);
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();

        var actor = await ActorAsync(client, Es256(HeimdallKey, subject));

        Assert.Equal(new ActorProbe(subject, false), actor);
        Assert.Equal(1, jwks.Requests);
        Assert.Equal(new ActorProbe(subject, false), await ActorAsync(client, Es256(HeimdallKey, subject)));
        Assert.Equal(1, jwks.Requests);
    }

    [FunctionalFact]
    public async Task GivenAnEs256TokenWithAnUnknownKid_WhenTheKeySetPublishesItLater_ThenOneFetchEachMinuteUntilItIsAccepted()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var jwks = new StubJwksHandler();
        await using var factory = CreateFactory(jwks, clock: clock);
        using var client = factory.CreateClient();
        var token = Es256(HeimdallKey, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, token));
        Assert.Equal(1, jwks.Requests);

        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, token));
        Assert.Equal(1, jwks.Requests);

        clock.Now += HeimdallJwks.MinimumRefreshInterval;

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, token));
        Assert.Equal(2, jwks.Requests);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, token));
        Assert.Equal(2, jwks.Requests);
    }

    [FunctionalFact]
    public async Task GivenABurstOfEs256TokensSignedWithAnUnpublishedKey_WhenRequested_ThenOneFetchServesThemAndEachIsRefused()
    {
        var jwks = new StubJwksHandler();
        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));
        await using var factory = CreateFactory(jwks);
        using var client = factory.CreateClient();
        var unpublished = JwksTestSupport.NewKey();

        var statuses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => StatusAsync(client, Es256(unpublished, Guid.NewGuid()))));

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(1, jwks.Requests);
    }

    [FunctionalFact]
    public async Task GivenHeimdallRotatesItsKey_WhenATokenNamesTheNewKey_ThenItIsFetchedAtMostOnceAMinute()
    {
        var newKey = JwksTestSupport.NewKey();
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var jwks = new StubJwksHandler();
        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));
        await using var factory = CreateFactory(jwks, clock: clock);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, Es256(HeimdallKey, Guid.NewGuid())));

        // Heimdall rotates: the new key signs, the old one stays published as the previous key.
        jwks.Serve(JwksTestSupport.KeySet(newKey, HeimdallKey));

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, Es256(newKey, Guid.NewGuid())));
        Assert.Equal(1, jwks.Requests);

        clock.Now += HeimdallJwks.MinimumRefreshInterval;

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, Es256(newKey, Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, Es256(HeimdallKey, Guid.NewGuid())));
        Assert.Equal(2, jwks.Requests);
    }

    /// <summary>Review Focus 1: Heimdall's key set is missing, failing, unreachable or garbled.</summary>
    /// <remarks>
    /// Only one fetch is allowed each minute, so the HS256 token is sent while that fetch is still unspent:
    /// first before any fetch, then again once the minute has passed. Were HS256 tokens to fetch, the stub
    /// would count it either time. With the retired secret still set, nothing falls back to it.
    /// </remarks>
    [FunctionalTheory]
    [InlineData("404")]
    [InlineData("500")]
    [InlineData("unreachable")]
    [InlineData("garbage")]
    [InlineData("no usable key")]
    public async Task GivenTheKeySetIsUnavailable_WhenAnHs256HeimdallTokenArrives_ThenItIsRefusedWithoutAFetch(string failure)
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var jwks = new StubJwksHandler { Respond = Failing(failure) };
        await using var factory = CreateFactory(jwks, retiredSecret: JwksTestSupport.SharedSecret, clock: clock);
        using var client = factory.CreateClient();
        var legacy = Hs256(JwksTestSupport.SharedSecret, Guid.NewGuid(), JwksTestSupport.Issuer);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, legacy));
        Assert.Equal(0, jwks.Requests);

        // An ES256 token cannot be verified without the key set: refused, never a server error.
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, Es256(HeimdallKey, Guid.NewGuid())));
        Assert.Equal(1, jwks.Requests);

        clock.Now += HeimdallJwks.MinimumRefreshInterval;

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, legacy));
        Assert.Equal(1, jwks.Requests);

        // The minute's fetch was there to spend: an ES256 token whose kid is still unknown spends it.
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, Es256(HeimdallKey, Guid.NewGuid())));
        Assert.Equal(2, jwks.Requests);
    }

    /// <summary>
    /// The secret Heimdall once signed with may still be set in an environment nobody has cleaned yet. It is
    /// no longer read: the HS256 token it signs is refused, while Heimdall's ES256 tokens and Fortuna's local
    /// tokens are served.
    /// </summary>
    [FunctionalFact]
    public async Task GivenTheRetiredSecretIsStillSet_WhenAnHs256HeimdallTokenSignedWithItArrives_ThenItIs401()
    {
        var jwks = new StubJwksHandler();
        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));
        await using var factory = CreateFactory(jwks, retiredSecret: JwksTestSupport.SharedSecret);
        using var client = factory.CreateClient();
        var local = factory.Services.GetRequiredService<ILocalAuthTokenIssuer>().Issue(Guid.NewGuid(), "Local User");

        Assert.Equal(JwksTestSupport.SharedSecret, Environment.GetEnvironmentVariable(SharedSecretSetting));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await StatusAsync(client, Hs256(JwksTestSupport.SharedSecret, Guid.NewGuid(), JwksTestSupport.Issuer)));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, Es256(HeimdallKey, Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, local.Token));
    }

    /// <summary>Once the retired secret is removed, as it should be: the app starts and serves.</summary>
    [FunctionalFact]
    public async Task GivenNoRetiredSecret_WhenTheAppStarts_ThenHeimdallAndLocalTokensAreServedAndHs256TokensRefused()
    {
        var jwks = new StubJwksHandler();
        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));
        await using var factory = CreateFactory(jwks);
        using var client = factory.CreateClient();
        var heimdallSubject = Guid.NewGuid();
        var localSubject = Guid.NewGuid();
        var local = factory.Services.GetRequiredService<ILocalAuthTokenIssuer>().Issue(localSubject, "Local User");

        Assert.Null(Environment.GetEnvironmentVariable(SharedSecretSetting));
        Assert.Equal(new ActorProbe(heimdallSubject, false), await ActorAsync(client, Es256(HeimdallKey, heimdallSubject)));
        Assert.Equal(new ActorProbe(localSubject, true), await ActorAsync(client, local.Token));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await StatusAsync(client, Hs256(JwksTestSupport.SharedSecret, Guid.NewGuid(), JwksTestSupport.Issuer)));
    }

    [FunctionalFact]
    public async Task GivenAnHs256TokenKeyedWithHeimdallsPublicKey_WhenRequested_ThenItIsRefused()
    {
        var jwks = new StubJwksHandler();
        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));
        await using var factory = CreateFactory(jwks);
        using var client = factory.CreateClient();

        // Make Heimdall's key known first, so the forged token's kid resolves to it and meets the EC key.
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, Es256(HeimdallKey, Guid.NewGuid())));

        var status = await StatusAsync(client, KeyedWithThePublicKey(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    /// <summary>
    /// Heimdall's HS256 tokens never named a kid, so one that does resolves to the published keys alone. When
    /// none matches there is no key at all: the scheme holds no static keys for IdentityModel to fall back to.
    /// </summary>
    [FunctionalFact]
    public async Task GivenAnHs256TokenOverTheRetiredSecretNamingAKid_WhenRequested_ThenItIsRefused()
    {
        await using var factory = CreateFactory(new StubJwksHandler(), retiredSecret: JwksTestSupport.SharedSecret);
        using var client = factory.CreateClient();
        var sharedSecret = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(JwksTestSupport.SharedSecret))
        {
            KeyId = "legacy-current"
        };
        var token = new JsonWebTokenHandler().CreateToken(Descriptor(
            Guid.NewGuid(), JwksTestSupport.Issuer, new SigningCredentials(sharedSecret, SecurityAlgorithms.HmacSha256)));

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, token));
    }

    [FunctionalFact]
    public async Task GivenALocalToken_WhenRequested_ThenTheActorIsLocal()
    {
        await using var factory = CreateFactory(new StubJwksHandler());
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();

        var actor = await ActorAsync(client, Hs256(JwksTestSupport.LocalSecret, subject, LocalIssuer));

        Assert.Equal(new ActorProbe(subject, true), actor);
    }

    [FunctionalFact]
    public async Task GivenALocalTokenFortunaIssues_WhenRequested_ThenItNamesTheLocalIssuerAndIsLocal()
    {
        await using var factory = CreateFactory(new StubJwksHandler());
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();

        var issued = factory.Services.GetRequiredService<ILocalAuthTokenIssuer>().Issue(subject, "Local User");

        Assert.Equal(LocalIssuer, new JsonWebToken(issued.Token).Issuer);
        Assert.True(await IsSignedWithAsync(issued.Token, JwksTestSupport.LocalSecret));
        Assert.False(await IsSignedWithAsync(issued.Token, JwksTestSupport.SharedSecret));
        Assert.Equal(new ActorProbe(subject, true), await ActorAsync(client, issued.Token));
    }

    [FunctionalFact]
    public async Task GivenAnEs256TokenClaimingTheLocalIssuer_WhenRequested_ThenItIsRefused()
    {
        var jwks = new StubJwksHandler();
        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));
        await using var factory = CreateFactory(jwks);
        using var client = factory.CreateClient();

        var status = await StatusAsync(client, Es256(HeimdallKey, Guid.NewGuid(), issuer: LocalIssuer));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [FunctionalFact]
    public async Task GivenALocalKeyTokenClaimingHeimdallsIssuer_WhenRequested_ThenItIsRefused()
    {
        await using var factory = CreateFactory(new StubJwksHandler());
        using var client = factory.CreateClient();

        var status = await StatusAsync(client, Hs256(JwksTestSupport.LocalSecret, Guid.NewGuid(), JwksTestSupport.Issuer));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [FunctionalFact]
    public async Task GivenALegacyLocalTokenUnderHeimdallsIssuer_WhenRequested_ThenItIsRefused()
    {
        // Fortuna used to sign local tokens with the shared secret, under Heimdall's issuer, with fortuna_local set.
        await using var factory = CreateFactory(new StubJwksHandler(), retiredSecret: JwksTestSupport.SharedSecret);
        using var client = factory.CreateClient();

        var status = await StatusAsync(
            client, Hs256(JwksTestSupport.SharedSecret, Guid.NewGuid(), JwksTestSupport.Issuer, localClaim: true));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    /// <summary>Review Focus 5.</summary>
    [FunctionalFact]
    public async Task GivenAnEs256HeimdallTokenClaimingLocal_WhenRequested_ThenTheActorIsNotLocal()
    {
        var jwks = new StubJwksHandler();
        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));
        await using var factory = CreateFactory(jwks, retiredSecret: JwksTestSupport.SharedSecret);
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();

        var actor = await ActorAsync(client, Es256(HeimdallKey, subject, localClaim: true));

        Assert.Equal(new ActorProbe(subject, false), actor);
    }

    [FunctionalTheory]
    [InlineData(JwksTestSupport.Issuer)]
    [InlineData(LocalIssuer)]
    public async Task GivenAnUnsignedToken_WhenRequested_ThenItIsRefused(string issuer)
    {
        await using var factory = CreateFactory(new StubJwksHandler());
        using var client = factory.CreateClient();
        var token = new JsonWebTokenHandler().CreateToken(Descriptor(Guid.NewGuid(), issuer, signingCredentials: null));

        Assert.Equal("none", new JsonWebToken(token).Alg);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, token));
    }

    /// <summary>
    /// The local secret verifies HS256 alone. The secret here is long enough for HS384 and HS512, so the key
    /// size cannot be what refuses them, and the HS256 token over the same secret shows it is accepted.
    /// </summary>
    [FunctionalTheory]
    [InlineData(SecurityAlgorithms.HmacSha384)]
    [InlineData(SecurityAlgorithms.HmacSha512)]
    public async Task GivenAnotherHmacAlgorithmOverTheLocalSecret_WhenRequested_ThenItIsRefused(string algorithm)
    {
        await using var factory = CreateFactory(new StubJwksHandler(), localSecret: LongLocalSecret);
        using var client = factory.CreateClient();
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(LongLocalSecret));
        var handler = new JsonWebTokenHandler();

        Assert.Equal(
            HttpStatusCode.OK,
            await StatusAsync(client, handler.CreateToken(Descriptor(
                Guid.NewGuid(), LocalIssuer, new SigningCredentials(key, SecurityAlgorithms.HmacSha256)))));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await StatusAsync(client, handler.CreateToken(Descriptor(
                Guid.NewGuid(), LocalIssuer, new SigningCredentials(key, algorithm)))));
    }

    /// <summary>
    /// The caller-key handler sends its headers only when a key is configured and the request being served
    /// has a client address. The test server gives requests no address, so without one here the handler
    /// would add nothing and this test could not tell whether it is on the key set's client.
    /// </summary>
    [FunctionalFact]
    public async Task GivenACallerKeyIsConfigured_WhenTheKeySetIsFetched_ThenNoCallerHeadersAreSent()
    {
        var jwks = new StubJwksHandler();
        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));
        await using var factory = CreateFactory(
            jwks,
            callerKey: "fortuna-tests-heimdall-caller-key-with-enough-length",
            clientAddress: IPAddress.Parse("203.0.113.7"));
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, Es256(HeimdallKey, Guid.NewGuid())));

        var sent = Assert.Single(jwks.RequestHeaders);
        Assert.DoesNotContain(HeimdallCallerHeadersHandler.CallerKeyHeader, sent, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(HeimdallCallerHeadersHandler.ClientAddressHeader, sent, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", sent, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whatever any handler would add, the key set's client has none of Fortuna's: every handler between
    /// the client and the stub is the HTTP client factory's own.
    /// </summary>
    [FunctionalFact]
    public async Task GivenTheRunningApp_WhenTheKeySetClientIsBuilt_ThenItHasNoHandlerOfFortunasOwn()
    {
        var jwks = new StubJwksHandler();
        await using var factory = CreateFactory(jwks);
        HttpMessageHandler? handler = factory.Services.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(HeimdallJwks.HttpClientName);
        List<Type> chain = [];

        while (handler is DelegatingHandler delegating)
        {
            chain.Add(delegating.GetType());
            handler = delegating.InnerHandler;
        }

        Assert.Same(jwks, handler);
        Assert.NotEmpty(chain);
        Assert.All(chain, type => Assert.Equal(typeof(IHttpMessageHandlerFactory).Assembly, type.Assembly));
    }

    /// <summary>
    /// The bearer scheme binds <c>Authentication:Schemes:Bearer</c> from configuration, which the process
    /// environment can set. An authority or metadata address there would make IdentityModel fetch a second
    /// key set and try its keys whenever Fortuna's resolver finds none, and a forward setting would hand
    /// authentication to another scheme; the scheme clears them all, so Heimdall's key set stays the only
    /// source of keys and the bearer scheme the only one that authenticates.
    /// </summary>
    [FunctionalTheory]
    [InlineData("Authority")]
    [InlineData("MetadataAddress")]
    [InlineData("Authority,MetadataAddress")]
    [InlineData("ForwardDefault")]
    [InlineData("ForwardAuthenticate")]
    public async Task GivenAnotherKeySourceInConfiguration_WhenTokensArrive_ThenOnlyHeimdallsKeySetIsFetched(string settings)
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var jwks = new StubJwksHandler();
        jwks.Serve(JwksTestSupport.KeySet(HeimdallKey));
        var secondSource = new SecondKeySource();
        var configuration = settings.Split(',').ToDictionary(
            setting => $"Authentication:Schemes:{JwtBearerDefaults.AuthenticationScheme}:{setting}",
            setting => (string?)(setting switch
            {
                "Authority" => SecondKeySource.Authority,
                "MetadataAddress" => SecondKeySource.MetadataAddress,
                _ => "Elsewhere"
            }));
        await using var factory = CreateFactory(
            jwks,
            retiredSecret: JwksTestSupport.SharedSecret,
            clock: clock,
            configuration: configuration,
            backchannel: secondSource);
        using var client = factory.CreateClient();
        var bearer = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, Es256(HeimdallKey, Guid.NewGuid())));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await StatusAsync(client, Es256(SecondKeySource.Key, Guid.NewGuid())));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await StatusAsync(client, Es256(SecondKeySource.Key, Guid.NewGuid(), issuer: SecondKeySource.Authority)));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await StatusAsync(client, Es256(SecondKeySource.Key, Guid.NewGuid(), issuer: LocalIssuer)));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            await StatusAsync(client, Hs256(JwksTestSupport.SharedSecret, Guid.NewGuid(), JwksTestSupport.Issuer)));
        Assert.Equal(
            HttpStatusCode.OK,
            await StatusAsync(client, Hs256(JwksTestSupport.LocalSecret, Guid.NewGuid(), LocalIssuer)));

        Assert.Equal(0, secondSource.Requests);
        Assert.Equal(1, jwks.Requests);
        Assert.Null(bearer.Authority);
        Assert.Null(bearer.MetadataAddress);
        Assert.Null(bearer.Configuration);
        Assert.Null(bearer.ConfigurationManager);
        Assert.False(bearer.TokenValidationParameters.TryAllIssuerSigningKeys);
        Assert.Null(bearer.ForwardDefault);
        Assert.Null(bearer.ForwardAuthenticate);
        Assert.Null(bearer.ForwardChallenge);
        Assert.Null(bearer.ForwardForbid);
        Assert.Null(bearer.ForwardSignIn);
        Assert.Null(bearer.ForwardSignOut);
        Assert.Null(bearer.ForwardDefaultSelector);
    }

    [FunctionalFact]
    public async Task GivenTheRunningApp_WhenHeimdallsKeySetIsResolved_ThenOneInstanceServesEveryRequest()
    {
        await using var factory = CreateFactory(new StubJwksHandler());
        using var scope = factory.Services.CreateScope();

        Assert.Same(
            factory.Services.GetRequiredService<HeimdallJwks>(),
            scope.ServiceProvider.GetRequiredService<HeimdallJwks>());
        Assert.Same(
            factory.Services.GetRequiredService<FortunaSigningKeys>(),
            scope.ServiceProvider.GetRequiredService<FortunaSigningKeys>());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        StubJwksHandler jwks,
        string? retiredSecret = null,
        string? localSecret = null,
        TimeProvider? clock = null,
        string? callerKey = null,
        IPAddress? clientAddress = null,
        IReadOnlyDictionary<string, string?>? configuration = null,
        HttpMessageHandler? backchannel = null)
    {
        var settings = JwksTestSupport.Settings();
        settings[SharedSecretSetting] = retiredSecret;
        settings["FORTUNA_LOCAL_TOKEN_SECRET"] = localSecret ?? JwksTestSupport.LocalSecret;
        settings["FORTUNA_DATA_CONNECTIONSTRING"] = UnreachableDatabase;
        settings[CallerKeySetting] = callerKey;

        foreach (var (key, value) in settings)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);

            if (configuration is not null)
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IUserProfileProvisioner>();
                services.AddSingleton<IUserProfileProvisioner, StubUserProfileProvisioner>();
                services.AddHttpClient(HeimdallJwks.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => jwks);

                if (clock is not null)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(clock);
                }

                if (clientAddress is not null)
                {
                    services.AddSingleton<IStartupFilter>(new ClientAddressStartupFilter(clientAddress));
                }

                if (backchannel is not null)
                {
                    // Where IdentityModel would send its metadata and key-set requests, were it given an address.
                    services.Configure<JwtBearerOptions>(
                        JwtBearerDefaults.AuthenticationScheme,
                        bearer => bearer.BackchannelHttpHandler = backchannel);
                }

                services.AddControllers().AddApplicationPart(typeof(TokenSchemesProbeController).Assembly);
            });
        });
    }

    private static Func<CancellationToken, Task<HttpResponseMessage>> Failing(string failure) => failure switch
    {
        "404" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
        "500" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
        "unreachable" => _ => throw new HttpRequestException("connection refused"),
        "garbage" => _ => Task.FromResult(StubJwksHandler.Json("<html>not a key set</html>")),
        "no usable key" => _ => Task.FromResult(StubJwksHandler.Json("""{"keys":[]}""")),
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
    };

    private static async Task<ActorProbe?> ActorAsync(HttpClient client, string token)
    {
        using var response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<ActorProbe>(CancellationToken.None);
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string token)
    {
        using var response = await SendAsync(client, token);

        return response.StatusCode;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ActorRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client.SendAsync(request, CancellationToken.None);
    }

    private static async Task<bool> IsSignedWithAsync(string token, string secret)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret)),
            ValidIssuer = LocalIssuer,
            ValidAudience = JwksTestSupport.Audience
        });

        return result.IsValid;
    }

    private static Dictionary<string, string> Claims(Guid subject, bool localClaim) =>
        new FortunaIdentityMapper().ToClaims(new FortunaIdentity(subject, (int)HeimdallRoles.User, null, [])
        {
            DisplayName = "Test User",
            IsLocal = localClaim
        });

    private static SecurityTokenDescriptor Descriptor(
        Guid subject,
        string issuer,
        SigningCredentials? signingCredentials) => new()
    {
        Issuer = issuer,
        Audience = JwksTestSupport.Audience,
        Claims = Claims(subject, localClaim: false).ToDictionary(claim => claim.Key, claim => (object)claim.Value),
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = signingCredentials
    };

    private static string Es256(JwtKey key, Guid subject, string issuer = JwksTestSupport.Issuer, bool localClaim = false) =>
        new JwtHandler().CreateToken(new JwtConfiguration(
            3600, issuer, JwksTestSupport.Audience, string.Empty, Claims(subject, localClaim))
        {
            Algorithm = JwtAlgorithms.ES256,
            Keys = [key],
            SigningKeyId = key.Id
        });

    private static string Hs256(string secret, Guid subject, string issuer, bool localClaim = false) =>
        new JwtHandler().CreateToken(new JwtConfiguration(
            3600, issuer, JwksTestSupport.Audience, secret, Claims(subject, localClaim)));

    /// <summary>An HS256 token keyed with Heimdall's public key PEM and naming Heimdall's kid: the classic confusion.</summary>
    private static string KeyedWithThePublicKey(Guid subject)
    {
        var publicKeyAsSecret = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(JwksTestSupport.PublicPemOf(HeimdallPem)))
        {
            KeyId = HeimdallKey.Id
        };
        var token = new JsonWebTokenHandler().CreateToken(Descriptor(
            subject, JwksTestSupport.Issuer, new SigningCredentials(publicKeyAsSecret, SecurityAlgorithms.HmacSha256)));
        var header = new JsonWebToken(token);

        Assert.Equal(SecurityAlgorithms.HmacSha256, header.Alg);
        Assert.Equal(HeimdallKey.Id, header.Kid);

        return token;
    }

    /// <summary>Gives every request the connection address a real client would have; the test server has none.</summary>
    private sealed class ClientAddressStartupFilter(IPAddress clientAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = clientAddress;

                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>
    /// A second issuer's metadata and key set, publishing a key Heimdall never had, and counting every request.
    /// </summary>
    private sealed class SecondKeySource : HttpMessageHandler
    {
        public const string Authority = "https://second-source.example.test/";
        public const string MetadataAddress = Authority + ".well-known/openid-configuration";
        private const string KeySetAddress = Authority + "keys";

        private int _requests;

        public static JwtKey Key { get; } = JwksTestSupport.NewKey();

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);

            return Task.FromResult(request.RequestUri?.AbsoluteUri switch
            {
                MetadataAddress => StubJwksHandler.Json(
                    $$"""{"issuer":"{{Authority}}","jwks_uri":"{{KeySetAddress}}"}"""),
                KeySetAddress => StubJwksHandler.Json(JwksTestSupport.KeySet(Key)),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        }
    }

    private sealed class StubUserProfileProvisioner : IUserProfileProvisioner
    {
        public Task<UserProfileSnapshot> GetOrCreateAsync(
            Guid externalSubject,
            string displayName,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;

            return Task.FromResult(new UserProfileSnapshot(
                Guid.NewGuid(), externalSubject, displayName, "BRL", false, now, now));
        }
    }
}

[ApiController]
[Route("__tests/token-schemes")]
public sealed class TokenSchemesProbeController(IRequestActorAccessor actorAccessor) : ControllerBase
{
    [HttpGet("actor")]
    public ActionResult<TokenSchemesTests.ActorProbe> Actor()
    {
        var actor = actorAccessor.Actor!;

        return Ok(new TokenSchemesTests.ActorProbe(actor.SubjectId, actor.IsLocal));
    }
}
