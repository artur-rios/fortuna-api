using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Shared.Accounts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Accounts;

public sealed class EfFinancialAccountStore(AppDbContext context)
    : IFinancialAccountStore, IFinancialAccountReader, IFinancialAccountUpdater
{
    public IQueryable<FinancialAccount> Query() => context.FinancialAccounts.AsNoTracking();

    public async Task<FinancialAccountSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken) => await context.FinancialAccounts
        .AsNoTracking()
        .Where(account =>
            account.User.PublicId == userId &&
            account.PublicId == id &&
            (includeDeleted || !account.IsDeleted))
        .Select(account => new FinancialAccountSnapshot(
            account.PublicId,
            account.User.PublicId,
            account.Name,
            account.Institution,
            account.AccountType,
            account.Currency.Code,
            account.OpeningBalance,
            account.IsDeleted,
            account.CreatedAt,
            account.UpdatedAt))
        .SingleOrDefaultAsync(cancellationToken);

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

    public async Task<FinancialAccountBalanceSnapshot?> CalculateBalanceAsync(
        Guid userId,
        Guid id,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var account = await context.FinancialAccounts
            .AsNoTracking()
            .Where(item =>
                item.User.PublicId == userId &&
                item.PublicId == id &&
                !item.IsDeleted)
            .Select(item => new
            {
                item.Id,
                item.UserId,
                item.PublicId,
                CurrencyCode = item.Currency.Code,
                item.OpeningBalance,
                item.CreatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            return null;
        }

        var openedOn = DateOnly.FromDateTime(account.CreatedAt.UtcDateTime);
        var movement = asOf < openedOn
            ? 0m
            : await context.FinancialTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.FinancialAccountId == account.Id &&
                    transaction.UserId == account.UserId &&
                    !transaction.IsDeleted &&
                    transaction.OccurredOn <= asOf)
                .Select(transaction => (decimal?)(transaction.Direction ==
                    Domain.Transactions.TransactionDirection.Earning
                        ? transaction.Amount
                        : -transaction.Amount))
                .SumAsync(cancellationToken) ?? 0m;

        return new FinancialAccountBalanceSnapshot(
            account.PublicId,
            account.CurrencyCode,
            account.OpeningBalance + movement,
            asOf);
    }

    public async Task<FinancialAccountUpdateResult> UpdateAsync(
        FinancialAccountUpdate update,
        CancellationToken cancellationToken)
    {
        var account = await context.FinancialAccounts
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleOrDefaultAsync(item =>
                item.User.PublicId == update.UserId &&
                item.PublicId == update.Id &&
                !item.IsDeleted,
                cancellationToken);
        if (account is null)
        {
            return new FinancialAccountUpdateResult(null, DuplicateName: false);
        }

        account.UpdateDetails(
            update.Name,
            update.Institution,
            update.AccountType,
            update.UpdatedAt);

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
            return new FinancialAccountUpdateResult(null, DuplicateName: true);
        }

        return new FinancialAccountUpdateResult(Snapshot(account), DuplicateName: false);
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
