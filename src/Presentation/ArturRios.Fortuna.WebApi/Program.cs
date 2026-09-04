using Amazon.Runtime;
using Amazon.S3;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Accounts;
using ArturRios.Fortuna.Data.Auditing;
using ArturRios.Fortuna.Data.Currencies;
using ArturRios.Fortuna.Data.Jobs;
using ArturRios.Fortuna.Data.Users;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Command.Handlers;
using ArturRios.Fortuna.Command.Auditing;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Input.Validation;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Command.Services;
using ArturRios.Fortuna.Integration.Ingestion;
using ArturRios.Fortuna.Integration.Rates;
using ArturRios.Fortuna.Integration.Storage;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.WebApi.Configuration;
using ArturRios.Fortuna.WebApi.Security;
using ArturRios.Fortuna.WebApi.Services;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Jwt;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Command.Interfaces;
using ArturRios.Mediator.Query;
using ArturRios.Mediator.Query.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using System.Text;

var options = FortunaOptions.From(Environment.GetEnvironmentVariable);
ConfigureLogging(options);

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton(new DatabaseDiagnosticsOptions(
        SensitiveDataLogging: false,
        DetailedErrors: !builder.Environment.IsProduction()));
    builder.Services.AddDbContext<AppDbContext>((services, database) => database.UseNpgsql(
        options.DataConnectionString,
        postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema)));
    builder.Services.AddScoped<DatabaseSeeder>();
    builder.Services.AddScoped<ICurrencyReader, EfCurrencyReader>();
    builder.Services.AddScoped<EfExchangeRateStore>();
    builder.Services.AddScoped<IExchangeRateStore>(provider =>
        provider.GetRequiredService<EfExchangeRateStore>());
    builder.Services.AddScoped<IExchangeRateReader>(provider =>
        provider.GetRequiredService<EfExchangeRateStore>());
    builder.Services.AddScoped<EfAuditEntryStore>();
    builder.Services.AddScoped<IAuditEntryStore>(provider =>
        provider.GetRequiredService<EfAuditEntryStore>());
    builder.Services.AddScoped<IAuditEntryReader>(provider =>
        provider.GetRequiredService<EfAuditEntryStore>());
    builder.Services.AddScoped<IAuditEntryWriter, AuditEntryWriter>();
    builder.Services.AddScoped<EfFinancialAccountStore>();
    builder.Services.AddScoped<IFinancialAccountStore>(provider =>
        provider.GetRequiredService<EfFinancialAccountStore>());
    builder.Services.AddScoped<IFinancialAccountReader>(provider =>
        provider.GetRequiredService<EfFinancialAccountStore>());
    builder.Services.AddScoped<IFinancialAccountUpdater>(provider =>
        provider.GetRequiredService<EfFinancialAccountStore>());
    builder.Services.AddSingleton(new PaginationOptions(options.PageSizeMaximum));
    builder.Services.AddScoped<IBackgroundJobStore, EfBackgroundJobStore>();
    builder.Services.AddSingleton<IBackgroundJobQueue>(new BackgroundJobQueue(options.JobQueueCapacity));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<BackgroundJobProcessor>();
    builder.Services.AddHostedService<DatabaseInitializationHostedService>();
    builder.Services.AddHostedService<BackgroundJobHostedService>();
    builder.Services.AddHostedService<ExchangeRateSyncHostedService>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IRequestActorAccessor, HttpContextRequestActorAccessor>();
    builder.Services.AddSingleton(new UserProfileProvisioningOptions(
        options.DefaultDisplayCurrency,
        options.Locale));
    builder.Services.AddScoped<EfUserProfileStore>();
    builder.Services.AddScoped<IUserProfileReader>(provider =>
        provider.GetRequiredService<EfUserProfileStore>());
    builder.Services.AddScoped<IUserProfileProvisioner>(provider =>
        provider.GetRequiredService<EfUserProfileStore>());
    builder.Services.AddSingleton(new LocalAccountOptions(
        options.LocalAuthEnabled,
        options.LocalAuthRecoveryCodeCount,
        options.DefaultDisplayCurrency,
        options.Locale));
    builder.Services.AddSingleton(new RateSyncOptions(
        options.RatesSourceBaseUri,
        options.RatesSyncCron,
        options.RatesCurrencies));
    builder.Services.AddSingleton<IRateLimitDelay, RateLimitDelay>();
    builder.Services.AddHttpClient<IPtaxRateClient, PtaxRateClient>(client =>
        client.BaseAddress = options.RatesSourceBaseUri ?? new Uri("http://localhost/"));
    builder.Services.AddScoped<IBackgroundJobHandler, ExchangeRateSyncJobHandler>();
    builder.Services.AddScoped<ILocalAccountStore, EfLocalAccountStore>();
    builder.Services.AddSingleton<ILocalCredentialStoreAvailability, LocalCredentialStoreAvailability>();
    builder.Services.AddSingleton<ILocalRecoveryCodeGenerator, LocalRecoveryCodeGenerator>();
    builder.Services.AddScoped<CommandMediator>();
    builder.Services.AddScoped<IValidator<CreateLocalAccountCommand>, CreateLocalAccountCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateLocalAccountCommand,
        CreateLocalAccountCommandOutput, CreateLocalAccountCommandHandler>();
    builder.Services.AddAuditedCommandHandler<AuthenticateLocalAccountCommand,
        AuthenticateLocalAccountCommandOutput, AuthenticateLocalAccountCommandHandler>();
    builder.Services.AddScoped<IValidator<RecoverLocalAccountCommand>, RecoverLocalAccountCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RecoverLocalAccountCommand,
        RecoverLocalAccountCommandOutput, RecoverLocalAccountCommandHandler>();
    builder.Services.AddAuditedCommandHandler<RegenerateLocalAccountRecoveryCodesCommand,
        RegenerateLocalAccountRecoveryCodesCommandOutput, RegenerateLocalAccountRecoveryCodesCommandHandler>();
    builder.Services.AddAuditedCommandHandler<SynchronizeExchangeRatesCommand,
        SynchronizeExchangeRatesCommandOutput, SynchronizeExchangeRatesCommandHandler>();
    builder.Services.AddScoped<IValidator<RecordManualExchangeRateCommand>,
        RecordManualExchangeRateCommandValidator>();
    builder.Services.AddAuditedCommandHandler<RecordManualExchangeRateCommand,
        RecordManualExchangeRateCommandOutput, RecordManualExchangeRateCommandHandler>();
    builder.Services.AddScoped<IValidator<CreateFinancialAccountCommand>,
        CreateFinancialAccountCommandValidator>();
    builder.Services.AddAuditedCommandHandler<CreateFinancialAccountCommand,
        CreateFinancialAccountCommandOutput, CreateFinancialAccountCommandHandler>();
    builder.Services.AddScoped<IValidator<UpdateFinancialAccountCommand>,
        UpdateFinancialAccountCommandValidator>();
    builder.Services.AddAuditedCommandHandler<UpdateFinancialAccountCommand,
        UpdateFinancialAccountCommandOutput, UpdateFinancialAccountCommandHandler>();
    builder.Services.AddScoped<QueryMediator>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetMyProfileQuery, UserProfileOutput>,
        GetMyProfileQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<ListSupportedCurrenciesQuery,
        ListSupportedCurrenciesQueryOutput>, ListSupportedCurrenciesQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetCurrencyByCodeQuery, CurrencyOutput>,
        GetCurrencyByCodeQueryHandler>();
    builder.Services.AddScoped<IValidator<ConvertFigureQuery>, ConvertFigureQueryValidator>();
    builder.Services.AddScoped<IQueryHandlerAsync<ConvertFigureQuery, ConvertFigureQueryOutput>,
        ConvertFigureQueryHandler>();
    builder.Services.AddScoped<IValidator<ListAuditEntriesQuery>, ListAuditEntriesQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListAuditEntriesQuery, AuditEntryOutput>,
        ListAuditEntriesQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetFinancialAccountByIdQuery, FinancialAccountOutput>,
        GetFinancialAccountByIdQueryHandler>();
    builder.Services.AddScoped<IQueryHandlerAsync<GetFinancialAccountBalanceQuery,
        FinancialAccountBalanceOutput>, GetFinancialAccountBalanceQueryHandler>();
    builder.Services.AddScoped<IValidator<ListFinancialAccountsQuery>, ListFinancialAccountsQueryValidator>();
    builder.Services.AddScoped<IPaginatedQueryHandlerAsync<ListFinancialAccountsQuery, FinancialAccountOutput>,
        ListFinancialAccountsQueryHandler>();

    builder.Services.AddSingleton<IIngestionSource, FileUploadIngestionSource>();
    builder.Services.AddSingleton<IngestionSourceRegistry>();
    RegisterAttachmentStore(builder.Services, options);

    builder.Services.AddControllers();
    var jwtConfiguration = BuildJwtConfiguration(options);
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
    builder.Services.AddAuthorization(authorization =>
        authorization.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());
    builder.Services.AddSingleton(jwtConfiguration);
    builder.Services.AddSingleton<JwtHandler>();
    builder.Services.AddSingleton<FortunaIdentityMapper>();
    builder.Services.AddSingleton<ILocalAuthTokenIssuer, LocalAuthTokenIssuer>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(document =>
    {
        document.SwaggerDoc("v1", new()
        {
            Title = "Fortuna API",
            Version = "v1"
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

    var app = builder.Build();
    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging(logging =>
        logging.GetLevel = (_, _, _) => LogEventLevel.Information);
    app.UseAuthentication();
    app.UseMiddleware<AuthenticatedActorMiddleware>();
    app.UseMiddleware<UserProfileProvisioningMiddleware>();
    app.UseAuthorization();
    app.MapControllers();
    app.Run();
}
finally
{
    await Log.CloseAndFlushAsync();
}

static void ConfigureLogging(FortunaOptions options)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(new JsonFormatter())
        .WriteTo.Map(
            logEvent => logEvent.Timestamp.ToString("yyyy/MM"),
            (yearMonth, sink) => sink.File(
                new JsonFormatter(),
                Path.Combine(options.LogDirectory, yearMonth, "fortuna-.json"),
                rollingInterval: RollingInterval.Day))
        .CreateLogger();
}

static void RegisterAttachmentStore(IServiceCollection services, FortunaOptions options)
{
    if (string.Equals(options.StorageProvider, "Filesystem", StringComparison.OrdinalIgnoreCase))
    {
        services.AddSingleton<IAttachmentStore>(new FilesystemAttachmentStore(options.StoragePath!));
        return;
    }

    var s3Config = new AmazonS3Config
    {
        ServiceURL = options.StorageS3Endpoint,
        ForcePathStyle = true
    };
    services.AddSingleton<IAmazonS3>(new AmazonS3Client(
        new BasicAWSCredentials(options.StorageS3AccessKey, options.StorageS3SecretKey),
        s3Config));
    services.AddSingleton<IAttachmentStore>(provider => new S3AttachmentStore(
        provider.GetRequiredService<IAmazonS3>(),
        options.StorageS3Bucket!));
}

static JwtConfiguration BuildJwtConfiguration(FortunaOptions options)
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

public partial class Program;
