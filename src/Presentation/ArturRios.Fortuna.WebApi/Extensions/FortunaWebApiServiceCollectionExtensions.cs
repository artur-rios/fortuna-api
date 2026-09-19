using System.Text;
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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace ArturRios.Fortuna.WebApi.Extensions;

public static class FortunaWebApiServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the HTTP surface: request identity, controllers, JWT authentication,
    ///     authorization, rate limiting, metrics and the OpenAPI document.
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
        services.AddPrometheusMetrics(options.MetricsPort);
        services.AddControllers()
            .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new ExactDecimalJsonConverter()))
            .ConfigureApiBehaviorOptions(api =>
                api.InvalidModelStateResponseFactory = ApiErrorResponses.InvalidModelState);
        services.Configure<ForwardedHeadersOptions>(forwarded =>
            ForwardedHeadersSetup.Configure(forwarded, options));
        var jwtConfiguration = BuildJwtConfiguration(options);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(authentication =>
            {
                authentication.MapInboundClaims = false;
                authentication.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = jwtConfiguration.Keys.Select(key =>
                        new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key.Secret))),
                    ValidateIssuer = true,
                    ValidIssuer = options.AuthTokenIssuer,
                    ValidateAudience = true,
                    ValidAudience = options.AuthTokenAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        services.AddAuthorization(authorization =>
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());
        services.AddRateLimiter(rateLimiting =>
        {
            rateLimiting.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiting.AddPolicy(AuthController.AnonymousRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });
        services.AddSingleton(jwtConfiguration);
        services.AddSingleton<JwtHandler>();
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

    private static JwtConfiguration BuildJwtConfiguration(FortunaOptions options)
    {
        List<JwtKey> keys = [new("current", options.AuthTokenSecret)];

        if (!string.IsNullOrWhiteSpace(options.AuthPreviousTokenSecret) &&
            options.AuthPreviousTokenSecret != options.AuthTokenSecret)
        {
            keys.Add(new JwtKey("previous", options.AuthPreviousTokenSecret));
        }

        return new JwtConfiguration(
            options.AuthTokenExpirationInSeconds,
            options.AuthTokenIssuer,
            options.AuthTokenAudience,
            options.AuthTokenSecret,
            [])
        {
            Keys = keys
        };
    }
}
