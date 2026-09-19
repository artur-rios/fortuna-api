using ArturRios.Fortuna.Data.Cards;
using ArturRios.Fortuna.Data.Classification;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Data.Currencies;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Lifecycle;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Transactions;
using ArturRios.Fortuna.Shared.Attachments;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Transactions;

public sealed class EfTransactionStore(
    AppDbContext context,
    IAttachmentLifecycleStore attachments)
    : ITransactionStore,
        ITransactionReader,
        ITransactionUpdater,
        ITransactionLifecycleStore,
        ITransactionReconciliationStore
{
    public IQueryable<TransactionReadSnapshot> Query(TransactionSearchCriteria criteria) =>
        Project(Filter(criteria));

    public Task<TransactionReadSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken) => Project(Filter(new TransactionSearchCriteria
        {
            UserId = userId,
            IncludeDeleted = includeDeleted
        })).SingleOrDefaultAsync(transaction => transaction.Id == id, cancellationToken);

    public async Task<IReadOnlyCollection<TransactionCurrencyTotalSnapshot>> SummarizeAsync(
        TransactionSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        var grouped = await Filter(criteria)
            .WhereLive()
            .WhereNotTransfer(context)
            .GroupBy(transaction => new
            {
                transaction.Currency.Code,
                transaction.Direction
            })
            .Select(group => new
            {
                CurrencyCode = group.Key.Code,
                group.Key.Direction,
                Amount = group.Sum(transaction => transaction.Amount)
            })
            .ToListAsync(cancellationToken);

        return grouped
            .GroupBy(total => total.CurrencyCode)
            .OrderBy(group => group.Key)
            .Select(group => new TransactionCurrencyTotalSnapshot(
                group.Key,
                group.SingleOrDefault(total =>
                    total.Direction == TransactionDirection.Expense)?.Amount ?? 0m,
                group.SingleOrDefault(total =>
                    total.Direction == TransactionDirection.Earning)?.Amount ?? 0m))
            .ToArray();
    }

    public async Task<TransactionRecordResult> RecordAsync(
        TransactionRecord record,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var account = await FindAccountAsync(record, cancellationToken);
        if (record.FinancialAccountId.HasValue && account is null)
        {
            return Result(TransactionRecordOutcome.FinancialAccountNotFound);
        }

        var card = await FindCardAsync(record, cancellationToken);
        if (record.CreditCardId.HasValue && card is null)
        {
            return Result(TransactionRecordOutcome.CreditCardNotFound);
        }

        var user = account?.User ?? card!.User;
        var category = await context.Categories.SingleOrDefaultAsync(item =>
            item.PublicId == record.CategoryId &&
            item.UserId == user.Id &&
            !item.IsDeleted,
            cancellationToken);
        if (category is null)
        {
            return Result(TransactionRecordOutcome.CategoryNotFound);
        }

        var targetCurrency = account?.Currency ?? card!.Currency;
        Currency? originalCurrency = null;
        ExchangeRate? exchangeRate = null;
        var amount = record.Amount;
        var sourceCode = string.IsNullOrWhiteSpace(record.CurrencyCode)
            ? targetCurrency.Code
            : record.CurrencyCode.Trim().ToUpperInvariant();
        if (sourceCode != targetCurrency.Code)
        {
            originalCurrency = await context.Currencies.SingleOrDefaultAsync(
                item => item.Code == sourceCode,
                cancellationToken);
            if (originalCurrency is null)
            {
                return Result(TransactionRecordOutcome.CurrencyNotSupported);
            }

            exchangeRate = await ExchangeRateLookup.FindLatestAsync(
                context,
                sourceCode,
                targetCurrency.Code,
                record.OccurredOn,
                cancellationToken);
            if (exchangeRate is null)
            {
                return Result(TransactionRecordOutcome.ExchangeRateUnavailable);
            }

            amount = decimal.Round(
                record.Amount * exchangeRate.Rate,
                targetCurrency.MinorUnitDigits,
                MidpointRounding.AwayFromZero);
            if (amount <= 0m)
            {
                return Result(TransactionRecordOutcome.ConvertedAmountTooSmall);
            }
        }

        var counterparty = await ClassificationResolver.GetOrCreateCounterpartyAsync(
            context,
            user,
            record.Counterparty,
            record.CreatedAt,
            cancellationToken);
        var tags = await ClassificationResolver.GetOrCreateTagsAsync(
            context,
            user,
            record.Tags,
            record.CreatedAt,
            cancellationToken);
        var transaction = account is not null
            ? new FinancialTransaction(
                user,
                account,
                category,
                record.Direction,
                amount,
                record.OccurredOn,
                record.CreatedAt,
                record.Description,
                counterparty,
                tags)
            : new FinancialTransaction(
                user,
                card!,
                category,
                record.Direction,
                amount,
                record.OccurredOn,
                record.CreatedAt,
                record.Description,
                counterparty,
                tags);
        if (exchangeRate is not null)
        {
            transaction.RecordForeignCurrencyDetails(
                record.Amount,
                originalCurrency!,
                exchangeRate.Rate,
                exchangeRate.RateDate,
                record.CreatedAt);
        }

        context.FinancialTransactions.Add(transaction);
        if (card is not null)
        {
            await CreditCardStatementResolver.AssignAsync(
                context,
                transaction,
                card,
                record.CreatedAt,
                cancellationToken);
            await CreditCardStatementResolver.RefreshTotalsAsync(
                context,
                [transaction.Statement],
                record.CreatedAt,
                cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return Result(TransactionRecordOutcome.Succeeded, new TransactionSnapshot(
            transaction.PublicId,
            account?.PublicId,
            card?.PublicId,
            category.PublicId,
            category.Name,
            transaction.Direction,
            transaction.Amount,
            targetCurrency.Code,
            transaction.OriginalAmount,
            originalCurrency?.Code,
            transaction.AppliedRate,
            transaction.RateDate,
            transaction.OccurredOn,
            transaction.Description,
            counterparty?.PublicId,
            counterparty?.Name,
            tags.Select(tag => new TransactionTagSnapshot(tag.PublicId, tag.Name)).ToArray(),
            transaction.Statement?.PublicId,
            transaction.Statement?.PeriodStart,
            transaction.Statement?.PeriodEnd,
            transaction.Statement?.ClosingDate,
            transaction.Statement?.DueDate,
            transaction.Statement?.Status.ToString(),
            transaction.Statement?.PurchaseTotal,
            transaction.IsLateArriving,
            transaction.CreatedAt,
            transaction.UpdatedAt));
    }

    public async Task<TransactionUpdateResult> UpdateAsync(
        TransactionUpdate update,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var transaction = await context.FinancialTransactions
            .Include(item => item.User)
            .Include(item => item.FinancialAccount)
                .ThenInclude(item => item!.Currency)
            .Include(item => item.CreditCard)
                .ThenInclude(item => item!.Currency)
            .Include(item => item.Statement)
            .Include(item => item.Category)
            .Include(item => item.Counterparty)
            .Include(item => item.Currency)
            .Include(item => item.OriginalCurrency)
            .Include(item => item.Tags)
            .SingleOrDefaultAsync(item =>
                item.User.PublicId == update.UserId &&
                item.PublicId == update.Id &&
                !item.IsDeleted,
                cancellationToken);
        if (transaction is null)
        {
            return UpdateResult(TransactionUpdateOutcome.NotFound);
        }

        var isTransfer = await context.Transfers.AnyAsync(transfer =>
            transfer.OutboundTransactionId == transaction.Id ||
            transfer.InboundTransactionId == transaction.Id,
            cancellationToken);
        if (isTransfer && (
            transaction.Amount != update.Amount ||
            transaction.Direction != update.Direction ||
            transaction.OccurredOn != update.OccurredOn ||
            !CounterpartyMatches(transaction.Counterparty, update.Counterparty)))
        {
            return UpdateResult(TransactionUpdateOutcome.TransferFieldsRestricted);
        }

        if (transaction.InstallmentPlanId.HasValue && (
            transaction.Amount != update.Amount ||
            transaction.Direction != update.Direction ||
            transaction.OccurredOn != update.OccurredOn))
        {
            return UpdateResult(TransactionUpdateOutcome.InstallmentFieldsRestricted);
        }

        var oldAmount = transaction.Amount;
        var oldDirection = transaction.Direction;
        var oldOccurredOn = transaction.OccurredOn;
        var oldStatement = transaction.Statement;
        var signedAmountChanged = oldAmount != update.Amount ||
            oldDirection != update.Direction;
        var cycleChanged = transaction.CreditCard is not null &&
            BillingCycle.Containing(
                oldOccurredOn,
                transaction.CreditCard.ClosingDay,
                transaction.CreditCard.DueDay) !=
            BillingCycle.Containing(
                update.OccurredOn,
                transaction.CreditCard.ClosingDay,
                transaction.CreditCard.DueDay);
        var requiresStatementAssignment = transaction.CreditCard is not null &&
            (oldStatement is null || cycleChanged);
        if (oldStatement?.Status == CreditCardStatementStatus.Settled &&
            (signedAmountChanged || requiresStatementAssignment))
        {
            return UpdateResult(TransactionUpdateOutcome.SettledStatementFrozen);
        }

        var category = await context.Categories.SingleOrDefaultAsync(item =>
            item.PublicId == update.CategoryId &&
            item.UserId == transaction.UserId &&
            !item.IsDeleted,
            cancellationToken);
        if (category is null)
        {
            return UpdateResult(TransactionUpdateOutcome.CategoryNotFound);
        }

        var counterparty = await ClassificationResolver.GetOrCreateCounterpartyAsync(
            context,
            transaction.User,
            update.Counterparty,
            update.UpdatedAt,
            cancellationToken);
        var tags = await ClassificationResolver.GetOrCreateTagsAsync(
            context,
            transaction.User,
            update.Tags,
            update.UpdatedAt,
            cancellationToken);
        transaction.UpdateDetails(
            category,
            update.Direction,
            update.Amount,
            update.OccurredOn,
            update.Description,
            counterparty,
            tags,
            update.UpdatedAt);
        if (transaction.CreditCard is not null && requiresStatementAssignment)
        {
            await CreditCardStatementResolver.AssignAsync(
                context,
                transaction,
                transaction.CreditCard,
                update.UpdatedAt,
                cancellationToken);
        }

        if (signedAmountChanged || requiresStatementAssignment)
        {
            await CreditCardStatementResolver.RefreshTotalsAsync(
                context,
                [oldStatement, transaction.Statement],
                update.UpdatedAt,
                cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);
        var snapshot = await FindByIdAsync(
            update.UserId,
            update.Id,
            includeDeleted: false,
            cancellationToken);

        return UpdateResult(TransactionUpdateOutcome.Succeeded, snapshot);
    }

    public async Task<TransactionReconciliationResult> ReconcileAsync(
        TransactionReconciliation change,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var transactionId = await context.FinancialTransactions
            .AsNoTracking()
            .Where(item =>
                item.User.PublicId == change.UserId &&
                item.PublicId == change.TransactionId)
            .Select(item => (long?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (transactionId is null)
        {
            return ReconciliationResult(
                TransactionReconciliationOutcome.TransactionNotFound);
        }

        await RowLock.LockForUpdateAsync<FinancialTransaction>(
            context,
            [transactionId.Value],
            cancellationToken);
        var transaction = await context.FinancialTransactions
            .Include(item => item.User)
            .Include(item => item.Statement)
            .SingleOrDefaultAsync(item =>
                item.Id == transactionId.Value &&
                !item.IsDeleted,
                cancellationToken);
        if (transaction is null)
        {
            return ReconciliationResult(
                TransactionReconciliationOutcome.TransactionNotFound);
        }

        if (transaction.Statement?.Status == CreditCardStatementStatus.Settled)
        {
            return ReconciliationResult(
                TransactionReconciliationOutcome.SettledStatementFrozen);
        }

        if (change.Unreconcile)
        {
            if (!transaction.IsReconciled)
            {
                return ReconciliationResult(
                    TransactionReconciliationOutcome.TransactionNotReconciled);
            }

            transaction.Unreconcile(change.ChangedAt);
            await context.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);

            return await SuccessfulReconciliationAsync(change, cancellationToken);
        }

        if (transaction.IsReconciled)
        {
            return ReconciliationResult(
                TransactionReconciliationOutcome.TransactionAlreadyReconciled);
        }

        var importedRecord = await context.ImportedRecords
            .Include(record => record.ImportJob)
                .ThenInclude(job => job.User)
            .SingleOrDefaultAsync(record =>
                record.Id == change.ImportedRecordId &&
                record.ImportJob.PublicId == change.ImportJobId &&
                record.ImportJob.User.PublicId == change.UserId &&
                record.Outcome != ImportedRecordOutcome.Rejected &&
                record.Amount.HasValue &&
                record.OccurredOn.HasValue,
                cancellationToken);
        if (importedRecord is null)
        {
            return ReconciliationResult(
                TransactionReconciliationOutcome.ImportedRecordNotFound);
        }

        var existing = await MatchedTransactionIdAsync(importedRecord.Id, cancellationToken);
        if (existing is not null)
        {
            return ReconciliationResult(
                TransactionReconciliationOutcome.ImportedRecordAlreadyMatched,
                existing);
        }

        transaction.Reconcile(importedRecord, change.ChangedAt);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseException.IsUniqueViolation(
            exception,
            FinancialTransactionMap.ImportedRecordIndex))
        {
            context.ChangeTracker.Clear();

            return ReconciliationResult(
                TransactionReconciliationOutcome.ImportedRecordAlreadyMatched,
                await MatchedTransactionIdAsync(importedRecord.Id, cancellationToken));
        }

        await databaseTransaction.CommitAsync(cancellationToken);

        return await SuccessfulReconciliationAsync(change, cancellationToken);
    }

    private Task<Guid?> MatchedTransactionIdAsync(
        long importedRecordId,
        CancellationToken cancellationToken) => context.FinancialTransactions
        .AsNoTracking()
        .Where(item => item.ImportedRecordId == importedRecordId)
        .Select(item => (Guid?)item.PublicId)
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<TransactionLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var transaction = await FindTrackedAsync(userId, id, cancellationToken);
        if (transaction is null)
        {
            return LifecycleResult(TransactionLifecycleOutcome.NotFound);
        }

        var transfer = await FindTransferAsync(transaction.Id, cancellationToken);
        var installmentPlan = transfer is null
            ? await FindInstallmentPlanAsync(transaction.Id, cancellationToken)
            : null;
        var transactionLegs = TransactionLegs(transaction, transfer, installmentPlan);
        if (await HasSettledStatementAsync(transactionLegs, cancellationToken))
        {
            return LifecycleResult(TransactionLifecycleOutcome.SettledStatementFrozen);
        }

        if (transfer is null && installmentPlan is null)
        {
            transaction.SoftDelete(changedAt);
        }
        else if (transfer is not null)
        {
            var cascadeId = transfer.SoftDelete(changedAt).CascadeId;
            foreach (var leg in transactionLegs)
            {
                SoftDeleteToCascade(leg, cascadeId, changedAt);
            }

            if (transfer.InboundInvestmentMovement is not null)
            {
                SoftDeleteToCascade(
                    transfer.InboundInvestmentMovement,
                    cascadeId,
                    changedAt);
            }
        }
        else
        {
            var cascadeId = installmentPlan!.SoftDelete(changedAt).CascadeId;
            foreach (var leg in transactionLegs)
            {
                SoftDeleteToCascade(leg, cascadeId, changedAt);
            }
        }

        await attachments.SoftDeleteForTransactionsAsync(
            DeletionCascades(transactionLegs),
            changedAt,
            cancellationToken);
        await CreditCardStatementResolver.RefreshTotalsAsync(
            context,
            transactionLegs.Select(leg => leg.Statement),
            changedAt,
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return LifecycleResult(TransactionLifecycleOutcome.Succeeded, transaction.PublicId);
    }

    public async Task<TransactionLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var transaction = await FindTrackedAsync(userId, id, cancellationToken);
        if (transaction is null)
        {
            return LifecycleResult(TransactionLifecycleOutcome.NotFound);
        }

        if (!transaction.IsDeleted)
        {
            return LifecycleResult(TransactionLifecycleOutcome.RestoreRequiresSoftDeletion);
        }

        var transfer = await FindTransferAsync(transaction.Id, cancellationToken);
        var installmentPlan = transfer is null
            ? await FindInstallmentPlanAsync(transaction.Id, cancellationToken)
            : null;
        var transactionLegs = TransactionLegs(transaction, transfer, installmentPlan);
        if (await HasSettledStatementAsync(transactionLegs, cancellationToken))
        {
            return LifecycleResult(TransactionLifecycleOutcome.SettledStatementFrozen);
        }

        var attachmentCascades = DeletionCascades(transactionLegs);

        if (transfer is null && installmentPlan is null)
        {
            transaction.Restore(changedAt);
        }
        else if (transfer is not null)
        {
            if (transfer.IsDeleted)
            {
                transfer.Restore(changedAt);
            }

            foreach (var leg in transactionLegs)
            {
                RestoreIfDeleted(leg, changedAt);
            }

            if (transfer.InboundInvestmentMovement is not null)
            {
                RestoreIfDeleted(transfer.InboundInvestmentMovement, changedAt);
            }
        }
        else
        {
            if (installmentPlan!.IsDeleted)
            {
                installmentPlan.Restore(changedAt);
            }

            foreach (var leg in transactionLegs)
            {
                RestoreIfDeleted(leg, changedAt);
            }
        }

        await attachments.RestoreForTransactionsAsync(
            attachmentCascades,
            changedAt,
            cancellationToken);
        await CreditCardStatementResolver.RefreshTotalsAsync(
            context,
            transactionLegs.Select(leg => leg.Statement),
            changedAt,
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return LifecycleResult(TransactionLifecycleOutcome.Succeeded, transaction.PublicId);
    }

    public async Task<TransactionLifecycleResult> HardDeleteAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var transaction = await FindTrackedAsync(userId, id, cancellationToken);
        if (transaction is null)
        {
            return LifecycleResult(TransactionLifecycleOutcome.NotFound);
        }

        var transfer = await FindTransferAsync(transaction.Id, cancellationToken);
        var installmentPlan = transfer is null
            ? await FindInstallmentPlanAsync(transaction.Id, cancellationToken)
            : null;
        var transactionLegs = TransactionLegs(transaction, transfer, installmentPlan);
        var allDeleted = transactionLegs.All(leg => leg.IsDeleted) &&
            (transfer is null || transfer.IsDeleted) &&
            (installmentPlan is null || installmentPlan.IsDeleted) &&
            (transfer?.InboundInvestmentMovement is null ||
                transfer.InboundInvestmentMovement.IsDeleted);
        if (!allDeleted)
        {
            return LifecycleResult(TransactionLifecycleOutcome.HardDeleteRequiresSoftDeletion);
        }

        if (await HasSettledStatementAsync(transactionLegs, cancellationToken))
        {
            return LifecycleResult(TransactionLifecycleOutcome.SettledStatementFrozen);
        }

        var removal = await attachments.RemoveForTransactionsAsync(
            transactionLegs.Select(leg => leg.Id).ToArray(),
            cancellationToken);
        if (!removal.StorageAvailable)
        {
            return LifecycleResult(TransactionLifecycleOutcome.AttachmentStorageUnavailable);
        }

        if (transfer is not null)
        {
            context.Transfers.Remove(transfer);
            if (transfer.InboundInvestmentMovement is not null)
            {
                context.InvestmentMovements.Remove(transfer.InboundInvestmentMovement);
            }
        }

        if (installmentPlan is not null)
        {
            context.InstallmentPlans.Remove(installmentPlan);
        }

        context.FinancialTransactions.RemoveRange(transactionLegs);
        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);
        await attachments.DeleteObjectsAsync(removal.StorageKeys, CancellationToken.None);

        return LifecycleResult(TransactionLifecycleOutcome.Succeeded, transaction.PublicId);
    }

    private Task<FinancialAccount?> FindAccountAsync(
        TransactionRecord record,
        CancellationToken cancellationToken) => record.FinancialAccountId.HasValue
        ? context.FinancialAccounts
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleOrDefaultAsync(item =>
                item.PublicId == record.FinancialAccountId.Value &&
                item.User.PublicId == record.UserId &&
                !item.IsDeleted,
                cancellationToken)
        : Task.FromResult<FinancialAccount?>(null);

    private Task<CreditCard?> FindCardAsync(
        TransactionRecord record,
        CancellationToken cancellationToken) => record.CreditCardId.HasValue
        ? context.CreditCards
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleOrDefaultAsync(item =>
                item.PublicId == record.CreditCardId.Value &&
                item.User.PublicId == record.UserId &&
                !item.IsDeleted,
                cancellationToken)
        : Task.FromResult<CreditCard?>(null);

    private Task<FinancialTransaction?> FindTrackedAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => context.FinancialTransactions
        .Include(item => item.Statement)
        .SingleOrDefaultAsync(item =>
            item.User.PublicId == userId &&
            item.PublicId == id,
            cancellationToken);

    private Task<Transfer?> FindTransferAsync(
        long transactionId,
        CancellationToken cancellationToken) => context.Transfers
        .Include(item => item.OutboundTransaction)
            .ThenInclude(item => item.Statement)
        .Include(item => item.InboundTransaction)
            .ThenInclude(item => item!.Statement)
        .Include(item => item.InboundInvestmentMovement)
        .SingleOrDefaultAsync(item =>
            item.OutboundTransactionId == transactionId ||
            item.InboundTransactionId == transactionId,
            cancellationToken);

    private Task<InstallmentPlan?> FindInstallmentPlanAsync(
        long transactionId,
        CancellationToken cancellationToken) => context.InstallmentPlans
        .Include(item => item.Installments)
            .ThenInclude(item => item.Statement)
        .SingleOrDefaultAsync(item =>
            item.Installments.Any(transaction => transaction.Id == transactionId),
            cancellationToken);

    private async Task<bool> HasSettledStatementAsync(
        IReadOnlyCollection<FinancialTransaction> transactions,
        CancellationToken cancellationToken)
    {
        if (transactions.Any(item =>
            item.Statement?.Status == CreditCardStatementStatus.Settled))
        {
            return true;
        }

        var transactionIds = transactions.Select(item => item.Id).ToArray();

        return await context.CreditCardStatements.AnyAsync(statement =>
            statement.Status == CreditCardStatementStatus.Settled &&
            statement.SettlementTransactionId.HasValue &&
            transactionIds.Contains(statement.SettlementTransactionId.Value),
            cancellationToken);
    }

    private IQueryable<FinancialTransaction> Filter(TransactionSearchCriteria criteria)
    {
        var transactions = context.FinancialTransactions
            .AsNoTracking()
            .Where(transaction => transaction.User.PublicId == criteria.UserId);
        if (!criteria.IncludeDeleted)
        {
            transactions = transactions.WhereLive();
        }

        if (criteria.From.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.OccurredOn >= criteria.From.Value);
        }

        if (criteria.To.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.OccurredOn <= criteria.To.Value);
        }

        if (criteria.FinancialAccountId.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.FinancialAccount != null &&
                transaction.FinancialAccount.PublicId == criteria.FinancialAccountId.Value);
        }

        if (criteria.CreditCardId.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.CreditCard != null &&
                transaction.CreditCard.PublicId == criteria.CreditCardId.Value);
        }

        if (criteria.CategoryId.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.Category.PublicId == criteria.CategoryId.Value);
        }

        if (criteria.TagId.HasValue)
        {
            transactions = transactions.Where(transaction => transaction.Tags.Any(tag =>
                tag.PublicId == criteria.TagId.Value && !tag.IsDeleted));
        }

        foreach (var tagId in criteria.RequiredTagIds)
        {
            var requiredTagId = tagId;
            transactions = transactions.Where(transaction => transaction.Tags.Any(tag =>
                tag.PublicId == requiredTagId && !tag.IsDeleted));
        }

        if (criteria.CounterpartyId.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.Counterparty != null &&
                transaction.Counterparty.PublicId == criteria.CounterpartyId.Value);
        }

        if (criteria.Direction.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.Direction == criteria.Direction.Value);
        }

        if (criteria.MinimumAmount.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.Amount >= criteria.MinimumAmount.Value);
        }

        if (criteria.MaximumAmount.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.Amount <= criteria.MaximumAmount.Value);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Text))
        {
            var text = criteria.Text.Trim().ToLowerInvariant();
            transactions = transactions.Where(transaction =>
                transaction.Description != null &&
                transaction.Description.ToLower().Contains(text));
        }

        return transactions;
    }

    private IQueryable<TransactionReadSnapshot> Project(
        IQueryable<FinancialTransaction> transactions) => transactions.Select(transaction =>
        new TransactionReadSnapshot
        {
            Id = transaction.PublicId,
            UserId = transaction.User.PublicId,
            FinancialAccountId = transaction.FinancialAccount == null
                ? null
                : transaction.FinancialAccount.PublicId,
            FinancialAccountName = transaction.FinancialAccount == null
                ? null
                : transaction.FinancialAccount.Name,
            CreditCardId = transaction.CreditCard == null
                ? null
                : transaction.CreditCard.PublicId,
            CreditCardName = transaction.CreditCard == null
                ? null
                : transaction.CreditCard.Name,
            CategoryId = transaction.Category.PublicId,
            CategoryName = transaction.Category.Name,
            CounterpartyId = transaction.Counterparty == null
                ? null
                : transaction.Counterparty.PublicId,
            CounterpartyName = transaction.Counterparty == null
                ? null
                : transaction.Counterparty.Name,
            Direction = transaction.Direction,
            Amount = transaction.Amount,
            CurrencyCode = transaction.Currency.Code,
            OriginalAmount = transaction.OriginalAmount,
            OriginalCurrencyCode = transaction.OriginalCurrency == null
                ? null
                : transaction.OriginalCurrency.Code,
            AppliedRate = transaction.AppliedRate,
            RateDate = transaction.RateDate,
            OccurredOn = transaction.OccurredOn,
            Description = transaction.Description,
            SourceType = transaction.SourceType,
            IsReconciled = transaction.IsReconciled,
            IsManuallyCorrected = transaction.IsManuallyCorrected,
            IsTransfer = context.Transfers.Any(transfer =>
                transfer.OutboundTransactionId == transaction.Id ||
                transfer.InboundTransactionId == transaction.Id),
            InstallmentPlanId = transaction.InstallmentPlan == null
                ? null
                : transaction.InstallmentPlan.PublicId,
            InstallmentNumber = transaction.InstallmentNumber,
            RecurringTransactionId = transaction.RecurringTransaction == null
                ? null
                : transaction.RecurringTransaction.PublicId,
            ImportJobId = transaction.ImportedRecord == null
                ? null
                : transaction.ImportedRecord.ImportJob.PublicId,
            ImportedRecordId = transaction.ImportedRecordId,
            ImportedAmount = transaction.ImportedRecord == null
                ? null
                : transaction.ImportedRecord.Amount,
            ImportedOccurredOn = transaction.ImportedRecord == null
                ? null
                : transaction.ImportedRecord.OccurredOn,
            StatementId = transaction.Statement == null
                ? null
                : transaction.Statement.PublicId,
            IsLateArriving = transaction.IsLateArriving,
            IsPossibleDuplicate = transaction.IsPossibleDuplicate,
            Tags = transaction.Tags
                .OrderBy(tag => tag.Name)
                .ThenBy(tag => tag.PublicId)
                .Select(tag => new TransactionReadTagSnapshot(tag.PublicId, tag.Name))
                .ToArray(),
            IsDeleted = transaction.IsDeleted,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt
        });

    private static bool CounterpartyMatches(Counterparty? current, string? requested)
    {
        var normalizedRequested = string.IsNullOrWhiteSpace(requested)
            ? null
            : requested.Trim().ToUpperInvariant();

        return current?.NormalizedName == normalizedRequested;
    }

    private static IReadOnlyCollection<FinancialTransaction> TransactionLegs(
        FinancialTransaction transaction,
        Transfer? transfer,
        InstallmentPlan? installmentPlan)
    {
        if (installmentPlan is not null)
        {
            return installmentPlan.Installments.ToArray();
        }

        return transfer?.InboundTransaction is null
            ? [transfer?.OutboundTransaction ?? transaction]
            : [transfer.OutboundTransaction, transfer.InboundTransaction];
    }

    private static IReadOnlyDictionary<long, Guid> DeletionCascades(
        IReadOnlyCollection<FinancialTransaction> transactions) => transactions
        .Where(transaction => transaction.IsDeleted && transaction.DeletionCascadeId.HasValue)
        .ToDictionary(
            transaction => transaction.Id,
            transaction => transaction.DeletionCascadeId!.Value);

    private static bool SoftDeleteToCascade(
        RecordLifecycleEntity entity,
        Guid cascadeId,
        DateTimeOffset changedAt)
    {
        var wasDeleted = entity.IsDeleted;
        if (entity.IsDeleted && entity.DeletionCascadeId != cascadeId)
        {
            entity.Restore(changedAt);
        }

        entity.SoftDeleteFromCascade(cascadeId, changedAt);

        return !wasDeleted;
    }

    private static bool RestoreIfDeleted(
        RecordLifecycleEntity entity,
        DateTimeOffset changedAt)
    {
        if (!entity.IsDeleted)
        {
            return false;
        }

        entity.Restore(changedAt);

        return true;
    }

    private static TransactionRecordResult Result(
        TransactionRecordOutcome outcome,
        TransactionSnapshot? transaction = null) => new(transaction, outcome);

    private static TransactionUpdateResult UpdateResult(
        TransactionUpdateOutcome outcome,
        TransactionReadSnapshot? transaction = null) => new(transaction, outcome);

    private async Task<TransactionReconciliationResult> SuccessfulReconciliationAsync(
        TransactionReconciliation change,
        CancellationToken cancellationToken)
    {
        var snapshot = await FindByIdAsync(
            change.UserId,
            change.TransactionId,
            includeDeleted: false,
            cancellationToken);

        return ReconciliationResult(
            TransactionReconciliationOutcome.Succeeded,
            transaction: snapshot);
    }

    private static TransactionReconciliationResult ReconciliationResult(
        TransactionReconciliationOutcome outcome,
        Guid? conflictingTransactionId = null,
        TransactionReadSnapshot? transaction = null) =>
        new(transaction, outcome, conflictingTransactionId);

    private static TransactionLifecycleResult LifecycleResult(
        TransactionLifecycleOutcome outcome,
        Guid? id = null) => new(id, outcome);
}
