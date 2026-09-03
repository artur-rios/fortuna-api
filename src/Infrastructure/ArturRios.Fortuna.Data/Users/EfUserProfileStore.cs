using System.Globalization;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Users;

/// <summary>Reads and atomically provisions local profiles for Heimdall identities.</summary>
public sealed class EfUserProfileStore(
    AppDbContext context,
    UserProfileProvisioningOptions options,
    TimeProvider timeProvider) : IUserProfileReader, IUserProfileProvisioner
{
    public async Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
        Guid externalSubject,
        CancellationToken cancellationToken)
    {
        var subject = externalSubject.ToString("D");
        var profile = await context.UserProfiles
            .AsNoTracking()
            .Include(x => x.DisplayCurrency)
            .SingleOrDefaultAsync(x => x.ExternalSubject == subject, cancellationToken);

        return profile is null ? null : Snapshot(profile);
    }

    public async Task<UserProfileSnapshot> GetOrCreateAsync(
        Guid externalSubject,
        string displayName,
        CancellationToken cancellationToken)
    {
        var existing = await FindByExternalSubjectAsync(externalSubject, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var currencyCode = options.DefaultDisplayCurrency ?? CurrencyForLocale(options.Locale);
        var currency = await context.Currencies.SingleOrDefaultAsync(
            x => x.Code == currencyCode,
            cancellationToken) ?? throw new InvalidOperationException(
                $"Display currency '{currencyCode}' is not present in the ISO 4217 reference set.");
        var profile = new UserProfile(
            externalSubject,
            displayName,
            currency,
            timeProvider.GetUtcNow());
        context.UserProfiles.Add(profile);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Snapshot(profile);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: UserProfileMap.ExternalSubjectIndex
            })
        {
            context.ChangeTracker.Clear();

            return await FindByExternalSubjectAsync(externalSubject, cancellationToken)
                ?? throw new InvalidOperationException(
                    "A concurrent profile provision completed but the profile could not be read.",
                    exception);
        }
    }

    private UserProfileSnapshot Snapshot(UserProfile profile) => new(
        profile.PublicId,
        Guid.Parse(profile.ExternalSubject!),
        profile.DisplayName,
        profile.DisplayCurrency.Code,
        options.DefaultDisplayCurrency is null,
        profile.CreatedAt,
        profile.UpdatedAt);

    private static string CurrencyForLocale(string locale) =>
        new RegionInfo(CultureInfo.GetCultureInfo(locale).Name).ISOCurrencySymbol;
}
