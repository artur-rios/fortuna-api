using System.Threading.RateLimiting;
using ArturRios.Fortuna.Data.Classification;
using ArturRios.Fortuna.Shared.Reporting;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Controllers;
using ArturRios.Fortuna.WebApi.Observability;
using ArturRios.Fortuna.WebApi.OpenApi;
using ArturRios.Fortuna.WebApi.Output;
using ArturRios.Fortuna.WebApi.Requests;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Fortuna.WebApi.Serialization;
using ArturRios.Fortuna.WebApi.Services;
using ArturRios.Jwt;
using ArturRios.Util.WebApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

namespace ArturRios.Fortuna.WebApi.Extensions;

public static class FortunaWebApiServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the HTTP surface: request identity, controllers, forwarded headers, request
    ///     tracing, JWT authentication, authorization, rate limiting, metrics and the OpenAPI document.
    /// </summary>
    public static IServiceCollection AddFortunaWebApi(
        this IServiceCollection services,
        FortunaOptions options)
    {
        services.AddSingleton<ITransactionDrillDownKeyCodec,
            DataProtectionTransactionDrillDownKeyCodec>();
        services.AddSingleton<UploadLimits>();
        services.AddHttpContextAccessor();
        services.AddScoped<IRequestActorAccessor, HttpContextRequestActorAccessor>();
        services.AddScoped<ICurrentProfileResolver, CurrentProfileResolver>();
        services.AddMemoryCache();
        services.AddSingleton<IProvisionedProfileCache, MemoryProvisionedProfileCache>();
        services.AddPrometheusMetrics(options.MetricsPort);
        services.AddControllers()
            .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new ExactDecimalJsonConverter()))
            .ConfigureApiBehaviorOptions(api =>
                api.InvalidModelStateResponseFactory = ApiErrorResponses.InvalidModelState);
        services.Configure<ForwardedHeadersOptions>(forwarded =>
            ForwardedHeadersSetup.Configure(forwarded, options));
        // Both are the library's defaults, set explicitly because writing the client IP address to
        // the logs, and tagging it on traces as client.address, is a decision about personal data
        // that the documentation declares rather than a technicality to inherit silently.
        services.Configure<TraceActivityOptions>(trace =>
        {
            trace.LogClientIp = true;
            trace.TagClientAddress = true;
        });
        AddBearerScheme(services, options);
        services.AddAuthorization(authorization =>
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());
        services.AddRateLimiter(rateLimiting =>
        {
            rateLimiting.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiting.AddPolicy(AuthController.AnonymousRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKey.For(context.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });
        services.AddSingleton<JwtHandler>();
        services.AddSingleton(LocalIdentityRule.From(options));
        services.AddSingleton<FortunaIdentityMapper>();
        services.AddSingleton<ILocalAuthTokenIssuer, LocalAuthTokenIssuer>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(document =>
        {
            document.SchemaFilter<EnumNamesSchemaFilter>();
            document.MapType<decimal>(() => new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "decimal",
                Pattern = @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$"
            });
            document.SwaggerDoc("v1", new()
            {
                Title = ApiContractMetadata.Service,
                Version = ApiContractMetadata.Version
            });
            var jwtSecurityScheme = new OpenApiSecurityScheme
            {
                BearerFormat = "JWT",
                Name = "JWT Authentication",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = JwtBearerDefaults.AuthenticationScheme,
                Description = "Enter the Heimdall JWT bearer token."
            };
            var jwtRequirement = new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme), [] }
            };

            document.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, jwtSecurityScheme);
            document.AddSecurityRequirement(_ => jwtRequirement);
        });

        return services;
    }

    /// <summary>
    ///     The bearer scheme. A token's issuer picks the keys that may verify it and each key verifies its
    ///     own algorithm only (<see cref="FortunaSigningKeys" />), so a token's <c>alg</c> header never
    ///     decides what kind of key checks it. Heimdall's ES256 keys come from its published key set
    ///     (<see cref="HeimdallJwks" />), fetched before validation when a token names one Fortuna does not
    ///     know (<see cref="HeimdallKeyRefresh" />); after validation the legacy <c>fortuna_local</c> claim
    ///     is removed from every token but a legacy one (<see cref="LocalIdentityRule" />).
    /// </summary>
    /// <remarks>
    ///     No static signing key, authority or configuration manager is set: IdentityModel falls back to
    ///     static keys when the resolver returns none, and the resolver must be the only source of keys.
    ///     The scheme binds <c>Authentication:Schemes:Bearer</c> from configuration, which the environment
    ///     can set, so the authority and metadata address are cleared after that binding: either one would
    ///     make IdentityModel fetch a second key set and try its keys whenever the resolver finds none. That
    ///     fallback to every key is turned off as well.
    ///     The key set's named client has no message handler of its own, so its fetch carries no caller
    ///     headers and no user's token.
    /// </remarks>
    private static void AddBearerScheme(IServiceCollection services, FortunaOptions options)
    {
        services.AddHttpClient(HeimdallJwks.HttpClientName);
        services.AddSingleton<HeimdallJwks>();
        services.AddSingleton<FortunaSigningKeys>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<FortunaSigningKeys, HeimdallJwks>((authentication, signingKeys, jwks) =>
            {
                authentication.MapInboundClaims = false;
                authentication.Authority = null;
                authentication.MetadataAddress = null!;
                authentication.Configuration = null;
                authentication.ConfigurationManager = null;
                authentication.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeyResolver = (_, securityToken, keyId, _) =>
                        signingKeys.Resolve(securityToken, keyId),
                    TryAllIssuerSigningKeys = false,
                    AlgorithmValidator = (algorithm, key, _, _) => FortunaSigningKeys.IsAllowed(algorithm, key),
                    ValidateIssuer = true,
                    ValidIssuers = [options.AuthTokenIssuer, options.LocalTokenIssuer],
                    ValidateAudience = true,
                    ValidAudience = options.AuthTokenAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
                authentication.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                        HeimdallKeyRefresh.BeforeValidationAsync(context, options.AuthTokenIssuer, jwks),
                    OnTokenValidated = context => LocalIdentityRule.OnTokenValidatedAsync(context, options)
                };
            });
        // The address and the issuers are not secrets; logging them makes a misconfigured consumer diagnosable.
        Log.Information(
            "Bearer tokens: Heimdall's ES256 keys from {JwksUrl}; Heimdall's HS256 tokens {LegacyState}; local tokens under issuer {LocalIssuer}",
            options.HeimdallJwksUri,
            options.AcceptsLegacyTokens ? "accepted until FORTUNA_AUTH_TOKEN_SECRET is removed" : "refused",
            options.LocalTokenIssuer);
    }
}
