using System.Globalization;
using System.Security.Cryptography;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Users;

public sealed class EfLocalAccountStore(
    AppDbContext context,
    LocalAccountOptions options) : ILocalAccountStore
{
    private const long AccountLockId = 0x464F5254554E4103;

    public Task<bool> ExistsAsync(CancellationToken cancellationToken) =>
        context.LocalAccounts.AsNoTracking().AnyAsync(cancellationToken);

    public async Task<LocalAccountCredentialSnapshot?> FindForAuthenticationAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var account = await context.LocalAccounts
            .AsNoTracking()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Name == name, cancellationToken);

        return account is null
            ? null
            : new LocalAccountCredentialSnapshot(
                account.User.PublicId,
                account.User.DisplayName,
                account.SecretHash,
                account.Salt);
    }

    public async Task<LocalAccountCredentialSnapshot?> FindForAuthenticationByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var account = await context.LocalAccounts
            .AsNoTracking()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.User.PublicId == userId, cancellationToken);

        return account is null
            ? null
            : new LocalAccountCredentialSnapshot(
                account.User.PublicId,
                account.User.DisplayName,
                account.SecretHash,
                account.Salt);
    }

    public async Task<LocalAccountCreationResult> CreateAsync(
        LocalAccountCreation creation,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AccountLockId})",
            cancellationToken);

        if (await context.LocalAccounts.AnyAsync(cancellationToken))
        {
            return new LocalAccountCreationResult(null, true);
        }

        var currencyCode = options.DefaultDisplayCurrency ?? CurrencyForLocale(options.Locale);
        var currency = await context.Currencies.SingleOrDefaultAsync(
            x => x.Code == currencyCode,
            cancellationToken) ?? throw new InvalidOperationException(
                $"Display currency '{currencyCode}' is not present in the ISO 4217 reference set.");
        var user = new UserProfile(creation.DisplayName, currency, creation.CreatedAt);
        var account = new LocalAccount(
            user,
            creation.DisplayName,
            creation.SecretHash,
            creation.Salt,
            creation.StorageMode,
            creation.CreatedAt);

        foreach (var codeHash in creation.RecoveryCodeHashes)
        {
            account.AddRecoveryCode(codeHash, creation.CreatedAt);
        }

        context.LocalAccounts.Add(account);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new LocalAccountCreationResult(
                new LocalAccountSnapshot(
                    account.PublicId,
                    user.PublicId,
                    account.Name,
                    account.StorageMode,
                    account.CreatedAt),
                false);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            return new LocalAccountCreationResult(null, true);
        }
    }

    public async Task<LocalAccountRecoveryResult> RecoverAsync(
        LocalAccountRecovery recovery,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AccountLockId})",
            cancellationToken);
        var account = await context.LocalAccounts
            .Include(x => x.User)
            .Include(x => x.RecoveryCodes)
            .SingleOrDefaultAsync(x => x.Name == recovery.Name, cancellationToken);

        if (account is null)
        {
            return new LocalAccountRecoveryResult(LocalAccountRecoveryStatus.InvalidCode, null);
        }

        var unusedCodes = account.RecoveryCodes
            .Where(code => code.UsedAt is null)
            .ToArray();
        if (unusedCodes.Length == 0)
        {
            return new LocalAccountRecoveryResult(LocalAccountRecoveryStatus.Exhausted, null);
        }

        var matchingCode = unusedCodes.FirstOrDefault(code =>
            CryptographicOperations.FixedTimeEquals(code.CodeHash, recovery.RecoveryCodeHash));
        if (matchingCode is null)
        {
            return new LocalAccountRecoveryResult(LocalAccountRecoveryStatus.InvalidCode, null);
        }

        matchingCode.MarkUsed(recovery.RecoveredAt);
        account.ReplaceSecret(recovery.NewSecretHash, recovery.NewSalt, recovery.RecoveredAt);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new LocalAccountRecoveryResult(
            LocalAccountRecoveryStatus.Recovered,
            new LocalAccountRecoverySnapshot(
                account.User.PublicId,
                account.User.DisplayName,
                unusedCodes.Length - 1));
    }

    public async Task<bool> RegenerateRecoveryCodesAsync(
        LocalAccountRecoveryCodeRegeneration regeneration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AccountLockId})",
            cancellationToken);
        var account = await context.LocalAccounts
            .Include(x => x.User)
            .Include(x => x.RecoveryCodes)
            .SingleOrDefaultAsync(x => x.User.PublicId == regeneration.UserId, cancellationToken);

        if (account is null ||
            !CryptographicOperations.FixedTimeEquals(
                account.SecretHash,
                regeneration.ExpectedSecretHash) ||
            !CryptographicOperations.FixedTimeEquals(
                account.Salt,
                regeneration.ExpectedSalt))
        {
            return false;
        }

        account.ReplaceRecoveryCodes(
            regeneration.RecoveryCodeHashes,
            regeneration.RegeneratedAt);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private static string CurrencyForLocale(string locale) =>
        new RegionInfo(CultureInfo.GetCultureInfo(locale).Name).ISOCurrencySymbol;
}
