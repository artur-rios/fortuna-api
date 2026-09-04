using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Shared.Accounts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Accounts;

public sealed class EfFinancialAccountStore(AppDbContext context) : IFinancialAccountStore
{
    public async Task<FinancialAccountCreationResult> CreateAsync(
        FinancialAccountCreation creation,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            profile => profile.PublicId == creation.UserId,
            cancellationToken);
        var currency = await context.Currencies.SingleAsync(
            item => item.Code == creation.CurrencyCode,
            cancellationToken);
        var account = new FinancialAccount(
            user,
            creation.Name,
            creation.Institution,
            creation.AccountType,
            currency,
            creation.OpeningBalance,
            creation.CreatedAt);
        context.FinancialAccounts.Add(account);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: FinancialAccountMap.LiveNameIndex
            })
        {
            context.Entry(account).State = EntityState.Detached;
            return new FinancialAccountCreationResult(null, DuplicateName: true);
        }

        return new FinancialAccountCreationResult(Snapshot(account), DuplicateName: false);
    }

    private static FinancialAccountSnapshot Snapshot(FinancialAccount account) => new(
        account.PublicId,
        account.User.PublicId,
        account.Name,
        account.Institution,
        account.AccountType,
        account.Currency.Code,
        account.OpeningBalance,
        account.IsDeleted,
        account.CreatedAt,
        account.UpdatedAt);
}
