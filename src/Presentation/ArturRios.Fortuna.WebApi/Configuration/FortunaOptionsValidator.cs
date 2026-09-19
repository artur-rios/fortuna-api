using System.Text;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Services;
using FluentValidation;

namespace ArturRios.Fortuna.WebApi.Configuration;

/// <summary>
/// The semantic rules of <see cref="FortunaOptions"/>: required settings, supported providers,
/// settings that depend on one another and minimum secret strength. Conversion of individual raw
/// values happens while parsing; this validator sees the converted options.
/// </summary>
public sealed class FortunaOptionsValidator : AbstractValidator<FortunaOptions>
{
    private const string Filesystem = "Filesystem";
    private const string S3 = "S3";

    public FortunaOptionsValidator()
    {
        Required(options => options.DataConnectionString, "FORTUNA_DATA_CONNECTIONSTRING");

        RuleFor(options => options.DataDatabaseType)
            .Cascade(CascadeMode.Stop)
            .Must(HasText)
            .WithMessage(ConfigurationMessages.Required("FORTUNA_DATA_DATABASETYPE"))
            .Must(DatabaseProvider.IsSupported)
            .WithMessage(ConfigurationMessages.DatabaseTypeUnsupported);

        RuleFor(options => options.StorageProvider)
            .Cascade(CascadeMode.Stop)
            .Must(HasText)
            .WithMessage(ConfigurationMessages.Required("FORTUNA_STORAGE_PROVIDER"))
            .Must(provider => IsProvider(provider, Filesystem) || IsProvider(provider, S3))
            .WithMessage(ConfigurationMessages.StorageProviderUnsupported);

        When(options => IsProvider(options.StorageProvider, Filesystem), () =>
            Required(options => options.StoragePath, "FORTUNA_STORAGE_PATH"));

        When(options => IsProvider(options.StorageProvider, S3), () =>
        {
            Required(options => options.StorageS3Endpoint, "FORTUNA_STORAGE_S3_ENDPOINT");
            Required(options => options.StorageS3Bucket, "FORTUNA_STORAGE_S3_BUCKET");
            Required(options => options.StorageS3AccessKey, "FORTUNA_STORAGE_S3_ACCESS_KEY");
            Required(options => options.StorageS3SecretKey, "FORTUNA_STORAGE_S3_SECRET_KEY");
        });

        Required(options => options.LogDirectory, "FORTUNA_LOG_DIRECTORY");

        RuleFor(options => options.AuthTokenSecret)
            .Cascade(CascadeMode.Stop)
            .Must(HasText)
            .WithMessage(ConfigurationMessages.Required("FORTUNA_AUTH_TOKEN_SECRET"))
            .Must(IsStrongSecret)
            .WithMessage(ConfigurationMessages.SecretTooShort(
                "FORTUNA_AUTH_TOKEN_SECRET",
                FortunaOptions.MinimumAuthTokenSecretBytes));

        RuleFor(options => options.AuthPreviousTokenSecret)
            .Must(secret => IsStrongSecret(secret!))
            .When(options => HasText(options.AuthPreviousTokenSecret))
            .WithMessage(ConfigurationMessages.SecretTooShort(
                "FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS",
                FortunaOptions.MinimumAuthTokenSecretBytes));

        Required(options => options.AuthTokenIssuer, "FORTUNA_AUTH_TOKEN_ISSUER");
        Required(options => options.AuthTokenAudience, "FORTUNA_AUTH_TOKEN_AUDIENCE");

        RuleFor(options => options.ConsentExternalDataProcessingVersion)
            .MaximumLength(50)
            .WithMessage(ConfigurationMessages.ConsentVersionTooLong);

        When(options => options.RatesSourceBaseUri is not null, () =>
        {
            RuleFor(options => options.RatesSyncCron)
                .Cascade(CascadeMode.Stop)
                .Must(HasText)
                .WithMessage(ConfigurationMessages.Required("FORTUNA_RATES_SYNC_CRON"))
                .Custom((cron, context) =>
                {
                    if (!CronSchedule.TryParse(cron, out _, out var error))
                    {
                        context.AddFailure(ConfigurationMessages.CronInvalid("FORTUNA_RATES_SYNC_CRON", error));
                    }
                });

            RuleFor(options => options.RatesCurrencies)
                .Must(currencies => currencies.Count >= 2)
                .WithMessage(ConfigurationMessages.RatesCurrenciesTooFew);
        });

        RuleFor(options => options.MetricsPort)
            .Must((options, port) => port <= 0 || options.ApiPorts.Count == 0 || options.ApiPorts.Any(api => api != port))
            .WithMessage(options => ConfigurationMessages.MetricsPortCollides("FORTUNA_METRICS_PORT", options.MetricsPort));
    }

    private void Required(System.Linq.Expressions.Expression<Func<FortunaOptions, string?>> setting, string key) =>
        RuleFor(setting)
            .Must(HasText)
            .WithMessage(ConfigurationMessages.Required(key));

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool IsProvider(string? value, string provider) =>
        string.Equals(value?.Trim(), provider, StringComparison.OrdinalIgnoreCase);

    // The signing key is derived from the secret's ASCII bytes (see Program.BuildJwtConfiguration).
    private static bool IsStrongSecret(string secret) =>
        Encoding.ASCII.GetByteCount(secret) >= FortunaOptions.MinimumAuthTokenSecretBytes;
}
