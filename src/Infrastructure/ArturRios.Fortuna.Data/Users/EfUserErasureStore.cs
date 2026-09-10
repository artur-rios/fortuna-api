using System.Data;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Users;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Users;

/// <summary>Executes the irreversible, dependency-ordered user erasure boundary.</summary>
public sealed class EfUserErasureStore(
    AppDbContext context,
    IAttachmentStore objects) : IUserErasureStore
{
    public async Task<UserErasureResult?> EraseAsync(
        Guid userId,
        DateTimeOffset erasedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var user = await context.UserProfiles.SingleOrDefaultAsync(
            item => item.PublicId == userId,
            cancellationToken);
        if (user is null)
        {
            return null;
        }

        var auditSubject = await context.AuditSubjects.SingleOrDefaultAsync(
            item => item.UserId == user.Id,
            cancellationToken);
        if (auditSubject is null)
        {
            auditSubject = new AuditSubject(user);
            context.AuditSubjects.Add(auditSubject);
            await context.SaveChangesAsync(cancellationToken);
        }
        var subjectReference = auditSubject.SubjectReference;

        var accountIds = await context.FinancialAccounts
            .Where(item => item.UserId == user.Id)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var cardIds = await context.CreditCards
            .Where(item => item.UserId == user.Id)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var investmentIds = await context.Investments
            .Where(item => item.UserId == user.Id)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var transactionIds = await context.FinancialTransactions
            .Where(item => item.UserId == user.Id)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var importJobs = await context.ImportJobs
            .Where(item => item.UserId == user.Id)
            .Select(item => new { item.Id, item.PublicId })
            .ToArrayAsync(cancellationToken);
        var exports = await context.DataExports
            .Where(item => item.UserId == user.Id)
            .Select(item => new { item.BackgroundJobId, item.StorageKey })
            .ToArrayAsync(cancellationToken);
        var importJobIds = importJobs.Select(item => item.Id).ToArray();
        var importJobPublicIds = importJobs.Select(item => item.PublicId).ToArray();
        var exportJobIds = exports.Select(item => item.BackgroundJobId).OfType<Guid>().ToArray();
        var importJobKeys = importJobPublicIds
            .SelectMany(id => new[]
            {
                $"excel-import:{id:N}",
                $"pdf-invoice-import:{id:N}",
                $"pluggy-synchronization:{id:N}"
            })
            .ToArray();
        var userPayloadMarker = user.PublicId.ToString("D");
        var backgroundJobIds = await context.BackgroundJobs
            .Where(item => exportJobIds.Contains(item.Id) ||
                           importJobKeys.Contains(item.IdempotencyKey) ||
                           (item.Type == "recurring-transaction-materialization" &&
                            item.Payload.Contains(userPayloadMarker)))
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var attachmentKeys = await context.Attachments
            .Where(item => transactionIds.Contains(item.TransactionId))
            .Select(item => item.StorageKey)
            .ToArrayAsync(cancellationToken);
        var objectKeys = attachmentKeys
            .Concat(exports.Select(item => item.StorageKey).OfType<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var objectBackups = await ReadObjectsAsync(objectKeys, cancellationToken);

        var movementIds = context.InvestmentMovements
            .Where(item => investmentIds.Contains(item.InvestmentId))
            .Select(item => item.Id);
        var importedRecordCount = await context.ImportedRecords.CountAsync(
            item => importJobIds.Contains(item.ImportJobId), cancellationToken);
        var connectionResourceCount = await context.ConnectionResources.CountAsync(
            item => item.Connection.UserId == user.Id, cancellationToken);
        var financialRecordCount = transactionIds.Length + accountIds.Length + cardIds.Length +
            investmentIds.Length +
            await context.Transfers.CountAsync(item =>
                transactionIds.Contains(item.OutboundTransactionId) ||
                (item.InboundTransactionId.HasValue && transactionIds.Contains(item.InboundTransactionId.Value)) ||
                (item.InboundInvestmentMovementId.HasValue && movementIds.Contains(item.InboundInvestmentMovementId.Value)),
                cancellationToken) +
            await context.CreditCardStatements.CountAsync(
                item => cardIds.Contains(item.CreditCardId), cancellationToken) +
            await context.InstallmentPlans.CountAsync(
                item => cardIds.Contains(item.CreditCardId), cancellationToken) +
            await context.InvestmentMovements.CountAsync(
                item => investmentIds.Contains(item.InvestmentId), cancellationToken) +
            await context.InvestmentValuations.CountAsync(
                item => investmentIds.Contains(item.InvestmentId), cancellationToken) +
            await context.Budgets.CountAsync(item => item.UserId == user.Id, cancellationToken) +
            await context.Goals.CountAsync(item => item.UserId == user.Id, cancellationToken) +
            await context.RecurringTransactions.CountAsync(
                item => item.UserId == user.Id, cancellationToken);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["attachments"] = attachmentKeys.Length,
            ["exports"] = exports.Length,
            ["imports"] = importJobs.Length + importedRecordCount,
            ["jobs"] = backgroundJobIds.Length,
            ["credentials"] = await context.LocalAccounts.CountAsync(
                item => item.UserId == user.Id, cancellationToken),
            ["recoveryCodes"] = await context.RecoveryCodes.CountAsync(
                item => item.LocalAccount.UserId == user.Id, cancellationToken),
            ["connections"] = await context.Connections.CountAsync(
                item => item.UserId == user.Id, cancellationToken) + connectionResourceCount,
            ["processingConsents"] = await context.ProcessingConsents.CountAsync(
                item => item.UserId == user.Id, cancellationToken),
            ["financialRecords"] = financialRecordCount,
            ["classificationRecords"] =
                await context.Categories.CountAsync(item => item.UserId == user.Id, cancellationToken) +
                await context.Tags.CountAsync(item => item.UserId == user.Id, cancellationToken) +
                await context.Counterparties.CountAsync(item => item.UserId == user.Id, cancellationToken),
            ["profiles"] = 1
        };

        var liveConnections = await context.Connections
            .Where(item => item.UserId == user.Id && item.Status != ConnectionStatus.Revoked)
            .ToListAsync(cancellationToken);
        foreach (var connection in liveConnections)
        {
            connection.Revoke(erasedAt);
        }

        await context.SaveChangesAsync(cancellationToken);

        try
        {
            await DeleteDependentsAsync(
                user.Id,
                accountIds,
                cardIds,
                investmentIds,
                transactionIds,
                importJobIds,
                backgroundJobIds,
                cancellationToken);

            if (liveConnections.Count > 0)
            {
                context.AuditEntries.Add(new AuditEntry(
                    subjectReference,
                    "RevokeConnectionsForUserErasure",
                    null,
                    null,
                    AuditOutcome.Succeeded,
                    null,
                    erasedAt));
            }
            context.AuditEntries.Add(new AuditEntry(
                subjectReference,
                "EraseUserCommand",
                null,
                null,
                AuditOutcome.Succeeded,
                null,
                erasedAt));
            await context.SaveChangesAsync(cancellationToken);

            // Bulk deletes deliberately bypass the change tracker; clear it before the two
            // identity roots so stale required navigations cannot trigger an implicit cascade.
            context.ChangeTracker.Clear();
            await context.AuditSubjects
                .Where(item => item.UserId == user.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await context.UserProfiles
                .Where(item => item.Id == user.Id)
                .ExecuteDeleteAsync(cancellationToken);

            foreach (var key in objectKeys)
            {
                await objects.DeleteAsync(key, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await RestoreObjectsAsync(objectBackups, CancellationToken.None);
            throw;
        }

        return new UserErasureResult(
            subjectReference,
            counts,
            liveConnections.Count);
    }

    private async Task DeleteDependentsAsync(
        long userId,
        long[] accountIds,
        long[] cardIds,
        long[] investmentIds,
        long[] transactionIds,
        long[] importJobIds,
        Guid[] backgroundJobIds,
        CancellationToken cancellationToken)
    {
        // Link tables are first so Restrict relationships remain an active safety net.
        await context.Set<Dictionary<string, object>>("FinancialTransactionTag")
            .Where(item => transactionIds.Contains(EF.Property<long>(item, "FinancialTransactionId")))
            .ExecuteDeleteAsync(cancellationToken);
        await context.Set<Dictionary<string, object>>("GoalAccount")
            .Where(item => accountIds.Contains(EF.Property<long>(item, "AccountId")))
            .ExecuteDeleteAsync(cancellationToken);
        await context.Set<Dictionary<string, object>>("GoalInvestment")
            .Where(item => investmentIds.Contains(EF.Property<long>(item, "InvestmentId")))
            .ExecuteDeleteAsync(cancellationToken);
        var categoryIds = context.Categories.Where(item => item.UserId == userId).Select(item => item.Id);
        await context.Set<Dictionary<string, object>>("BudgetCategory")
            .Where(item => categoryIds.Contains(EF.Property<long>(item, "CategoryId")))
            .ExecuteDeleteAsync(cancellationToken);

        await context.Attachments
            .Where(item => transactionIds.Contains(item.TransactionId))
            .ExecuteDeleteAsync(cancellationToken);
        var movementIds = context.InvestmentMovements
            .Where(item => investmentIds.Contains(item.InvestmentId))
            .Select(item => item.Id);
        await context.Transfers.Where(item =>
                transactionIds.Contains(item.OutboundTransactionId) ||
                (item.InboundTransactionId.HasValue && transactionIds.Contains(item.InboundTransactionId.Value)) ||
                (item.InboundInvestmentMovementId.HasValue && movementIds.Contains(item.InboundInvestmentMovementId.Value)))
            .ExecuteDeleteAsync(cancellationToken);
        await context.CreditCardStatements
            .Where(item => cardIds.Contains(item.CreditCardId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.SettlementTransactionId, (long?)null), cancellationToken);
        await context.FinancialTransactions
            .Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.InvestmentValuations
            .Where(item => investmentIds.Contains(item.InvestmentId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.InvestmentMovements
            .Where(item => investmentIds.Contains(item.InvestmentId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.CreditCardStatements
            .Where(item => cardIds.Contains(item.CreditCardId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.InstallmentPlans
            .Where(item => cardIds.Contains(item.CreditCardId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.RecurringTransactions
            .Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await context.ConnectionResources
            .Where(item => item.Connection.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ImportedRecords
            .Where(item => importJobIds.Contains(item.ImportJobId))
            .ExecuteDeleteAsync(cancellationToken);
        await context.ImportJobs
            .Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.DataExports
            .Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.BackgroundJobs
            .Where(item => backgroundJobIds.Contains(item.Id))
            .ExecuteDeleteAsync(cancellationToken);

        await context.Budgets.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await context.Goals.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await context.Investments.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await context.CreditCards.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await context.FinancialAccounts.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await context.Categories.Where(item => item.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ParentId, (long?)null), cancellationToken);
        await context.Categories.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await context.Tags.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await context.Counterparties.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await context.Connections.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await context.ProcessingConsents.Where(item => item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.RecoveryCodes
            .Where(item => item.LocalAccount.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.LocalAccounts.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<Dictionary<string, byte[]>> ReadObjectsAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken)
    {
        var backups = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            try
            {
                await using var source = await objects.OpenReadAsync(key, cancellationToken);
                using var copy = new MemoryStream();
                await source.CopyToAsync(copy, cancellationToken);
                backups[key] = copy.ToArray();
            }
            catch (AttachmentObjectNotFoundException)
            {
                // A missing object is already physically erased; its metadata is still removed.
            }
        }

        return backups;
    }

    private async Task RestoreObjectsAsync(
        IReadOnlyDictionary<string, byte[]> backups,
        CancellationToken cancellationToken)
    {
        foreach (var (key, content) in backups)
        {
            await using var stream = new MemoryStream(content, writable: false);
            await objects.WriteAsync(key, stream, cancellationToken);
        }
    }
}
