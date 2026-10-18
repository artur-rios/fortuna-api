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

        // Optional since Heimdall signs ES256: while it is set, Heimdall's HS256 tokens from before that are
        // still accepted (the overlap), so it is held to the strength it always had.
        RuleFor(options => options.AuthTokenSecret)
            .Must(secret => IsStrongSecret(secret!))
            .When(options => options.AcceptsLegacyTokens)
            .WithMessage(ConfigurationMessages.SecretTooShort(
                "FORTUNA_AUTH_TOKEN_SECRET",
                FortunaOptions.MinimumAuthTokenSecretBytes));

        RuleFor(options => options.AuthPreviousTokenSecret)
            .Must((options, _) => options.AcceptsLegacyTokens)
            .When(options => HasText(options.AuthPreviousTokenSecret))
            .WithMessage(ConfigurationMessages.PreviousWithoutCurrent(
                "FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS",
                "FORTUNA_AUTH_TOKEN_SECRET"));

        // Fortuna's own key: never shared, so never equal to a secret other services hold.
        RuleFor(options => options.LocalTokenSecret)
            .Cascade(CascadeMode.Stop)
            .Must(HasText)
            .WithMessage(ConfigurationMessages.Required("FORTUNA_LOCAL_TOKEN_SECRET"))
            .Must(IsStrongSecret)
            .WithMessage(ConfigurationMessages.SecretTooShort(
                "FORTUNA_LOCAL_TOKEN_SECRET",
                FortunaOptions.MinimumAuthTokenSecretBytes))
            .Must((options, secret) => secret != options.AuthTokenSecret)
            .WithMessage(ConfigurationMessages.MustDiffer("FORTUNA_LOCAL_TOKEN_SECRET", "FORTUNA_AUTH_TOKEN_SECRET"))
            .Must((options, secret) => secret != options.AuthPreviousTokenSecret)
            .WithMessage(ConfigurationMessages.MustDiffer(
                "FORTUNA_LOCAL_TOKEN_SECRET",
                "FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS"));

        // The issuer is what tells a local token from a Heimdall one.
        RuleFor(options => options.LocalTokenIssuer)
            .Must((options, issuer) => !string.Equals(issuer, options.AuthTokenIssuer?.Trim(), StringComparison.Ordinal))
            .WithMessage(ConfigurationMessages.MustDiffer("FORTUNA_LOCAL_TOKEN_ISSUER", "FORTUNA_AUTH_TOKEN_ISSUER"));

        RuleFor(options => options.AuthPreviousTokenSecret)
            .Must(secret => IsStrongSecret(secret!))
            .When(options => HasText(options.AuthPreviousTokenSecret))
            .WithMessage(ConfigurationMessages.SecretTooShort(
                "FORTUNA_AUTH_TOKEN_SECRET_PREVIOUS",
                FortunaOptions.MinimumAuthTokenSecretBytes));

        // The key travels as an HTTP header value, so anything outside visible ASCII (a space, a
        // line break, a non-ASCII letter) would make every request to Heimdall throw instead of
        // failing once, clearly, here. A comma is refused too: Heimdall splits
        // HEIMDALL_TRUSTED_CALLER_KEYS on commas, so such a key can never be one of its keys, and
        // Heimdall answers a wrong key by silently counting the request under Fortuna's address.
        RuleFor(options => options.HeimdallCallerKey)
            .Cascade(CascadeMode.Stop)
            .Must(key => key!.Length >= FortunaOptions.MinimumHeimdallCallerKeyLength)
            .WithMessage(ConfigurationMessages.SecretTooShortCharacters(
                "FORTUNA_HEIMDALL_CALLER_KEY",
                FortunaOptions.MinimumHeimdallCallerKeyLength))
            .Must(key => key!.All(character => IsVisibleAscii(character) && character != ','))
            .WithMessage(ConfigurationMessages.CallerKeyCharacters("FORTUNA_HEIMDALL_CALLER_KEY"))
            .When(options => HasText(options.HeimdallCallerKey));

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

    private static bool IsVisibleAscii(char character) => character is >= '!' and <= '~';

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool IsProvider(string? value, string provider) =>
        string.Equals(value?.Trim(), provider, StringComparison.OrdinalIgnoreCase);

    // An HS256 key is derived from the secret's ASCII bytes, so its strength is counted in those bytes.
    private static bool IsStrongSecret(string secret) =>
        Encoding.ASCII.GetByteCount(secret) >= FortunaOptions.MinimumAuthTokenSecretBytes;
}
