using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Ingestion;

public sealed class EfExcelImportStore(AppDbContext context) : IExcelImportStore
{
    public async Task<QueueExcelImportResult> QueueAsync(
        ExcelImportRequest request,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            item => item.PublicId == request.UserId,
            cancellationToken);
        var target = await TargetAsync(request, cancellationToken);
        if (target is null)
        {
            return Result(QueueExcelImportOutcome.TargetNotFound);
        }

        if (target.IsDeleted)
        {
            return Result(QueueExcelImportOutcome.TargetDeleted);
        }

        var importJob = new ImportJob(user, TransactionSourceType.Excel, request.CreatedAt);
        var payload = JsonSerializer.Serialize(new ExcelImportJobPayload(
            importJob.PublicId,
            request.UserId,
            request.TargetId,
            request.TargetType,
            request.Content,
            request.Mapping,
            request.CreateMissingCategories));
        var backgroundJob = BackgroundJob.Create(
            ExcelImportJob.Type,
            payload,
            $"{ExcelImportJob.Type}:{importJob.PublicId:N}",
            request.CorrelationId,
            request.CreatedAt);
        context.ImportJobs.Add(importJob);
        context.BackgroundJobs.Add(backgroundJob);
        await context.SaveChangesAsync(cancellationToken);
        return new QueueExcelImportResult(
            Snapshot(importJob),
            backgroundJob.Id,
            QueueExcelImportOutcome.Succeeded);
    }

    public async Task<bool> BeginAsync(
        Guid importJobId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs.SingleOrDefaultAsync(
            item => item.PublicId == importJobId &&
                item.SourceType == TransactionSourceType.Excel,
            cancellationToken);
        if (job is null || job.Status is ImportJobStatus.Completed or ImportJobStatus.Failed)
        {
            return false;
        }

        if (job.Status == ImportJobStatus.Pending)
        {
            job.Start(startedAt);
            await context.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task CompleteAsync(
        Guid importJobId,
        Guid userId,
        Guid targetId,
        ImportTargetType targetType,
        bool createMissingCategories,
        IReadOnlyCollection<ExcelWorkbookRow> rows,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs.Include(item => item.User).SingleAsync(
            item => item.PublicId == importJobId &&
                item.User.PublicId == userId &&
                item.SourceType == TransactionSourceType.Excel,
            cancellationToken);
        if (job.Status != ImportJobStatus.Running)
        {
            return;
        }

        var account = targetType == ImportTargetType.Account
            ? await context.FinancialAccounts.Include(item => item.Currency).SingleOrDefaultAsync(
                item => item.UserId == job.UserId && item.PublicId == targetId && !item.IsDeleted,
                cancellationToken)
            : null;
        var card = targetType == ImportTargetType.CreditCard
            ? await context.CreditCards.Include(item => item.Currency).SingleOrDefaultAsync(
                item => item.UserId == job.UserId && item.PublicId == targetId && !item.IsDeleted,
                cancellationToken)
            : null;
        if ((account is null) == (card is null))
        {
            throw new InvalidOperationException("The Excel import target is unavailable.");
        }

        var imported = 0;
        var duplicates = 0;
        var rejected = 0;
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.RejectionReason is not null || row.OccurredOn is null ||
                row.Amount is not > 0 || row.Direction is null)
            {
                context.ImportedRecords.Add(new ImportedRecord(
                    job,
                    row.RawPayload,
                    ImportedRecordOutcome.Rejected,
                    row.Amount,
                    row.OccurredOn,
                    ValidExternalId(row.ExternalId),
                    row.RejectionReason));
                rejected++;
                continue;
            }

            var signature = Signature(row.OccurredOn.Value, row.Amount.Value, row.ExternalId);
            var duplicate = !signatures.Add(signature) || await IsDuplicateAsync(
                account,
                card,
                row.OccurredOn.Value,
                row.Amount.Value,
                row.ExternalId,
                cancellationToken);
            if (duplicate)
            {
                context.ImportedRecords.Add(new ImportedRecord(
                    job,
                    row.RawPayload,
                    ImportedRecordOutcome.Duplicate,
                    row.Amount,
                    row.OccurredOn,
                    ValidExternalId(row.ExternalId)));
                duplicates++;
                continue;
            }

            var category = await ResolveCategoryAsync(
                job.User,
                row.Category,
                createMissingCategories,
                completedAt,
                cancellationToken);
            var record = new ImportedRecord(
                job,
                row.RawPayload,
                ImportedRecordOutcome.Imported,
                row.Amount,
                row.OccurredOn,
                ValidExternalId(row.ExternalId));
            var transaction = account is not null
                ? new FinancialTransaction(
                    job.User,
                    account,
                    category,
                    row.Direction.Value,
                    row.Amount.Value,
                    row.OccurredOn.Value,
                    completedAt,
                    Description(row.Description))
                : new FinancialTransaction(
                    job.User,
                    card!,
                    category,
                    row.Direction.Value,
                    row.Amount.Value,
                    row.OccurredOn.Value,
                    completedAt,
                    Description(row.Description));
            transaction.MarkAsImported(record, TransactionSourceType.Excel, completedAt);
            if (card is not null)
            {
                await AssignToStatementAsync(transaction, card, completedAt, cancellationToken);
            }

            context.ImportedRecords.Add(record);
            context.FinancialTransactions.Add(transaction);
            imported++;
        }

        job.Complete(imported, duplicates, rejected, completedAt);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(
        Guid importJobId,
        string reason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs.SingleAsync(
            item => item.PublicId == importJobId &&
                item.SourceType == TransactionSourceType.Excel,
            cancellationToken);
        if (job.Status is ImportJobStatus.Pending or ImportJobStatus.Running)
        {
            job.Fail(reason, failedAt);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Domain.Lifecycle.RecordLifecycleEntity?> TargetAsync(
        ExcelImportRequest request,
        CancellationToken cancellationToken) => request.TargetType switch
        {
            ImportTargetType.Account => await context.FinancialAccounts.SingleOrDefaultAsync(
                item => item.User.PublicId == request.UserId && item.PublicId == request.TargetId,
                cancellationToken),
            ImportTargetType.CreditCard => await context.CreditCards.SingleOrDefaultAsync(
                item => item.User.PublicId == request.UserId && item.PublicId == request.TargetId,
                cancellationToken),
            _ => null
        };

    private async Task<Category> ResolveCategoryAsync(
        UserProfile user,
        string? requested,
        bool createMissing,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(requested) || !createMissing
            ? "Uncategorized"
            : requested.Trim();
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var requestedCategory = await FindCategoryAsync(user.Id, requested, cancellationToken);
            if (requestedCategory is not null)
            {
                return requestedCategory;
            }
        }

        var category = await FindCategoryAsync(user.Id, name, cancellationToken);
        if (category is not null)
        {
            return category;
        }

        category = new Category(user, name, createdAt);
        context.Categories.Add(category);
        return category;
    }

    private Task<Category?> FindCategoryAsync(
        long userId,
        string name,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        var local = context.Categories.Local.SingleOrDefault(item =>
            item.UserId == userId && item.ParentId == null &&
            item.NormalizedName == normalized && !item.IsDeleted);
        return local is not null
            ? Task.FromResult<Category?>(local)
            : context.Categories.Include(item => item.User).SingleOrDefaultAsync(item =>
                item.UserId == userId && item.ParentId == null &&
                item.NormalizedName == normalized && !item.IsDeleted,
                cancellationToken);
    }

    private async Task<bool> IsDuplicateAsync(
        FinancialAccount? account,
        CreditCard? card,
        DateOnly occurredOn,
        decimal amount,
        string? externalId,
        CancellationToken cancellationToken)
    {
        var matches = context.FinancialTransactions.AsNoTracking().Where(item =>
            !item.IsDeleted && item.OccurredOn == occurredOn && item.Amount == amount &&
            item.FinancialAccountId == (account == null ? null : account.Id) &&
            item.CreditCardId == (card == null ? null : card.Id));
        return string.IsNullOrWhiteSpace(externalId)
            ? await matches.AnyAsync(cancellationToken)
            : await matches.AnyAsync(item =>
                item.ImportedRecord != null && item.ImportedRecord.ExternalId == externalId,
                cancellationToken);
    }

    private async Task AssignToStatementAsync(
        FinancialTransaction transaction,
        CreditCard card,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var cycle = BillingCycle.Containing(transaction.OccurredOn, card.ClosingDay, card.DueDay);
        var statement = await context.CreditCardStatements.SingleOrDefaultAsync(item =>
            item.CreditCardId == card.Id && item.PeriodStart == cycle.PeriodStart &&
            item.PeriodEnd == cycle.PeriodEnd && !item.IsDeleted,
            cancellationToken);
        statement ??= context.CreditCardStatements.Local.SingleOrDefault(item =>
            item.CreditCard == card && item.PeriodStart == cycle.PeriodStart &&
            item.PeriodEnd == cycle.PeriodEnd);
        if (statement is null)
        {
            statement = new CreditCardStatement(card, cycle, changedAt);
            context.CreditCardStatements.Add(statement);
        }

        var existingTotal = statement.Id == 0
            ? 0m
            : await context.FinancialTransactions
                .Where(item => item.StatementId == statement.Id && !item.IsDeleted)
                .Select(item => (decimal?)(item.Direction == TransactionDirection.Expense
                    ? item.Amount
                    : -item.Amount))
                .SumAsync(cancellationToken) ?? 0m;
        var localTotal = context.FinancialTransactions.Local
            .Where(item => item.Statement == statement && !item.IsDeleted)
            .Sum(item => item.Direction == TransactionDirection.Expense
                ? item.Amount
                : -item.Amount);
        transaction.AssignToStatement(statement, false, changedAt);
        statement.RecalculatePurchaseTotal(
            existingTotal + localTotal + (transaction.Direction == TransactionDirection.Expense
                ? transaction.Amount
                : -transaction.Amount),
            changedAt);
    }

    private static string Signature(DateOnly date, decimal amount, string? externalId) =>
        $"{date:O}|{amount}|{externalId}";

    private static string? ValidExternalId(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > 200 ? null : value.Trim();

    private static string? Description(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim()[..Math.Min(value.Trim().Length, 500)];

    private static QueueExcelImportResult Result(QueueExcelImportOutcome outcome) =>
        new(null, null, outcome);

    private static ExcelImportJobSnapshot Snapshot(ImportJob job) => new(
        job.PublicId,
        job.Status,
        job.CreatedAt,
        job.UpdatedAt);
}
