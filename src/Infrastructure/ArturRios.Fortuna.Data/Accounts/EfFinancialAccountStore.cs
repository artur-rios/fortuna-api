using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Data.Transactions;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Shared.Accounts;
using ArturRios.Fortuna.Shared.Attachments;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Accounts;

public sealed class EfFinancialAccountStore(
    AppDbContext context,
    IAttachmentLifecycleStore attachments)
    : IFinancialAccountStore, IFinancialAccountReader, IFinancialAccountUpdater,
        IFinancialAccountLifecycleStore
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
        var user = await context.UserProfiles.SingleOrDefaultAsync(
            profile => profile.PublicId == creation.UserId,
            cancellationToken);
        if (user is null)
        {
            return new FinancialAccountCreationResult(
                null,
                DuplicateName: false,
                FinancialAccountCreationOutcome.ProfileNotFound);
        }

        var currency = await context.Currencies.SingleOrDefaultAsync(
            item => item.Code == creation.CurrencyCode,
            cancellationToken);
        if (currency is null)
        {
            return new FinancialAccountCreationResult(
                null,
                DuplicateName: false,
                FinancialAccountCreationOutcome.CurrencyNotSupported);
        }

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
            DatabaseException.IsUniqueViolation(exception, FinancialAccountMap.LiveNameIndex))
        {
            context.Entry(account).State = EntityState.Detached;

            return new FinancialAccountCreationResult(
                null,
                DuplicateName: true,
                FinancialAccountCreationOutcome.DuplicateName);
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

        var balances = await AccountBalanceCalculator.CalculateAsync(
            context,
            [new AccountOpening(account.Id, account.OpeningBalance, account.CreatedAt)],
            asOf,
            cancellationToken);

        return new FinancialAccountBalanceSnapshot(
            account.PublicId,
            account.CurrencyCode,
            balances[account.Id],
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
            DatabaseException.IsUniqueViolation(exception, FinancialAccountMap.LiveNameIndex))
        {
            context.Entry(account).State = EntityState.Detached;

            return new FinancialAccountUpdateResult(null, DuplicateName: true);
        }

        return new FinancialAccountUpdateResult(Snapshot(account), DuplicateName: false);
    }

    public async Task<FinancialAccountLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var account = await FindTrackedAsync(userId, id, cancellationToken);
        if (account is null)
        {
            return LifecycleResult(FinancialAccountLifecycleOutcome.NotFound);
        }

        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var deletion = account.SoftDelete(changedAt);
        var transactions = await context.FinancialTransactions
            .Where(item => item.FinancialAccountId == account.Id)
            .ToListAsync(cancellationToken);
        foreach (var transaction in transactions)
        {
            transaction.SoftDeleteFromCascade(deletion.CascadeId, changedAt);
        }

        var linkedLegs = await TransferCascade.SoftDeleteAsync(
            context,
            transactions,
            deletion.CascadeId,
            changedAt,
            cancellationToken);
        await attachments.SoftDeleteForTransactionsAsync(
            CascadedTransactions(transactions.Concat(linkedLegs), deletion.CascadeId),
            changedAt,
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return LifecycleResult(FinancialAccountLifecycleOutcome.Succeeded, account.PublicId);
    }

    public async Task<FinancialAccountLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var account = await FindTrackedAsync(userId, id, cancellationToken);
        if (account is null)
        {
            return LifecycleResult(FinancialAccountLifecycleOutcome.NotFound);
        }

        if (!account.IsDeleted)
        {
            return LifecycleResult(FinancialAccountLifecycleOutcome.RestoreRequiresSoftDeletion);
        }

        var transactions = await context.FinancialTransactions
            .Where(item => item.FinancialAccountId == account.Id)
            .ToListAsync(cancellationToken);
        var cascadeId = account.Restore(changedAt);
        foreach (var transaction in transactions)
        {
            transaction.RestoreFromCascade(cascadeId, changedAt);
        }

        var linkedLegs = await TransferCascade.RestoreAsync(
            context,
            transactions,
            cascadeId,
            changedAt,
            cancellationToken);
        await attachments.RestoreForTransactionsAsync(
            transactions.Concat(linkedLegs)
                .Where(transaction => !transaction.IsDeleted)
                .ToDictionary(transaction => transaction.Id, _ => cascadeId),
            changedAt,
            cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            DatabaseException.IsUniqueViolation(exception, FinancialAccountMap.LiveNameIndex))
        {
            context.ChangeTracker.Clear();

            return LifecycleResult(FinancialAccountLifecycleOutcome.DuplicateName);
        }

        return LifecycleResult(FinancialAccountLifecycleOutcome.Succeeded, account.PublicId);
    }

    public async Task<FinancialAccountLifecycleResult> HardDeleteAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await FindTrackedAsync(userId, id, cancellationToken);
        if (account is null)
        {
            return LifecycleResult(FinancialAccountLifecycleOutcome.NotFound);
        }

        var transactions = await context.FinancialTransactions
            .Where(item => item.FinancialAccountId == account.Id)
            .ToListAsync(cancellationToken);
        var liveReferences = transactions.Any(item => !item.IsDeleted)
            ? new[] { "transactions" }
            : [];
        var check = account.CheckHardDeletion(liveReferences);
        if (!check.IsAllowed)
        {
            return LifecycleResult(
                check.Conflict == RecordLifecycleConflict.HardDeleteRequiresSoftDeletion
                    ? FinancialAccountLifecycleOutcome.HardDeleteRequiresSoftDeletion
                    : FinancialAccountLifecycleOutcome.HardDeleteHasLiveTransactions);
        }

        if (await HasDependentsAsync(account.Id, cancellationToken))
        {
            return LifecycleResult(FinancialAccountLifecycleOutcome.HardDeleteHasDependents);
        }

        var deletion = await TransactionHardDeletion.PlanAsync(
            context,
            transactions,
            [],
            cancellationToken);
        if (deletion.LiveReferences.Count > 0)
        {
            return LifecycleResult(FinancialAccountLifecycleOutcome.HardDeleteHasDependents);
        }

        var removal = await attachments.RemoveForTransactionsAsync(
            deletion.TransactionIds,
            cancellationToken);
        if (!removal.StorageAvailable)
        {
            return LifecycleResult(FinancialAccountLifecycleOutcome.AttachmentStorageUnavailable);
        }

        deletion.Remove(context);
        context.FinancialAccounts.Remove(account);
        await context.SaveChangesAsync(cancellationToken);
        await attachments.DeleteObjectsAsync(removal.StorageKeys, CancellationToken.None);

        return LifecycleResult(FinancialAccountLifecycleOutcome.Succeeded, account.PublicId);
    }

    private async Task<bool> HasDependentsAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        await context.Goals.AnyAsync(
            goal => goal.Accounts.Any(account => account.Id == accountId),
            cancellationToken) ||
        await context.RecurringTransactions.AnyAsync(
            rule => rule.FinancialAccountId == accountId,
            cancellationToken) ||
        await context.ConnectionResources.AnyAsync(
            resource => resource.FinancialAccountId == accountId,
            cancellationToken);

    private Task<FinancialAccount?> FindTrackedAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => context.FinancialAccounts
        .SingleOrDefaultAsync(item =>
            item.User.PublicId == userId &&
            item.PublicId == id,
            cancellationToken);

    private static IReadOnlyDictionary<long, Guid> CascadedTransactions(
        IEnumerable<Domain.Transactions.FinancialTransaction> transactions,
        Guid cascadeId) => transactions
        .Where(transaction => transaction.DeletionCascadeId == cascadeId)
        .ToDictionary(transaction => transaction.Id, _ => cascadeId);

    private static FinancialAccountLifecycleResult LifecycleResult(
        FinancialAccountLifecycleOutcome outcome,
        Guid? id = null) => new(id, outcome);

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
