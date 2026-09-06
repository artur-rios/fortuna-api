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
using ArturRios.Fortuna.Shared.Messages;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Ingestion;

public sealed class EfPluggySynchronizationStore(AppDbContext context)
    : IPluggySynchronizationStore
{
    public async Task<QueueSynchronizationResult> QueueAsync(
        Guid userId,
        Guid connectionId,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        string? correlationId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var connection = await context.Connections
            .Include(item => item.User)
            .SingleOrDefaultAsync(item =>
                item.PublicId == connectionId && item.User.PublicId == userId,
                cancellationToken);
        if (connection is null)
        {
            return Result(QueueSynchronizationOutcome.ConnectionNotFound);
        }

        if (connection.Status == ConnectionStatus.RequiresReauthentication)
        {
            return Result(QueueSynchronizationOutcome.ConnectionRequiresReauthentication);
        }

        if (connection.Status == ConnectionStatus.Revoked)
        {
            return Result(QueueSynchronizationOutcome.ConnectionRevoked);
        }

        if (connection.Status != ConnectionStatus.Active)
        {
            return Result(QueueSynchronizationOutcome.ConnectionInactive);
        }

        var existing = await context.ImportJobs.SingleOrDefaultAsync(item =>
            item.ConnectionId == connection.Id &&
            (item.Status == ImportJobStatus.Pending || item.Status == ImportJobStatus.Running),
            cancellationToken);
        if (existing is not null)
        {
            return new QueueSynchronizationResult(
                Snapshot(existing, connection.PublicId),
                null,
                QueueSynchronizationOutcome.AlreadyRunning);
        }

        var importJob = new ImportJob(
            connection.User,
            connection,
            periodStart,
            periodEnd,
            createdAt);
        var payload = JsonSerializer.Serialize(new PluggySynchronizationJobPayload(
            importJob.PublicId));
        var backgroundJob = BackgroundJob.Create(
            PluggySynchronizationJob.Type,
            payload,
            $"{PluggySynchronizationJob.Type}:{importJob.PublicId:N}",
            correlationId,
            createdAt);
        context.ImportJobs.Add(importJob);
        context.BackgroundJobs.Add(backgroundJob);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_import_job_connection_unfinished"
            })
        {
            context.Entry(importJob).State = EntityState.Detached;
            context.Entry(backgroundJob).State = EntityState.Detached;
            var winner = await context.ImportJobs.SingleAsync(item =>
                item.ConnectionId == connection.Id &&
                (item.Status == ImportJobStatus.Pending || item.Status == ImportJobStatus.Running),
                cancellationToken);
            return new QueueSynchronizationResult(
                Snapshot(winner, connection.PublicId),
                null,
                QueueSynchronizationOutcome.AlreadyRunning);
        }

        return new QueueSynchronizationResult(
            Snapshot(importJob, connection.PublicId),
            backgroundJob.Id,
            QueueSynchronizationOutcome.Succeeded);
    }

    public async Task<PluggySynchronizationContext?> BeginAsync(
        Guid importJobId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs
            .Include(item => item.Connection)
            .SingleOrDefaultAsync(item => item.PublicId == importJobId, cancellationToken);
        if (job?.Connection is null || job.Status == ImportJobStatus.Failed ||
            job.Status == ImportJobStatus.Completed)
        {
            return null;
        }

        if (job.Status == ImportJobStatus.Pending)
        {
            job.Start(startedAt);
            await context.SaveChangesAsync(cancellationToken);
        }

        return new PluggySynchronizationContext(
            job.PublicId,
            job.Connection.PublicId,
            job.Connection.ExternalReference,
            job.Connection.AccessTokenCipher.ToArray(),
            job.PeriodStart,
            job.PeriodEnd);
    }

    public async Task CompleteAsync(
        Guid importJobId,
        PluggySynchronizationBatch batch,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs
            .Include(item => item.Connection)
                .ThenInclude(item => item!.User)
            .SingleAsync(item => item.PublicId == importJobId, cancellationToken);
        var connection = job.Connection
            ?? throw new InvalidOperationException("A Pluggy import job requires a connection.");
        var mappings = await context.ConnectionResources
            .Include(item => item.FinancialAccount)
                .ThenInclude(item => item!.Currency)
            .Include(item => item.CreditCard)
                .ThenInclude(item => item!.Currency)
            .Where(item => item.ConnectionId == connection.Id)
            .ToDictionaryAsync(item => item.ExternalReference, StringComparer.Ordinal,
                cancellationToken);

        foreach (var resource in batch.Resources)
        {
            if (mappings.ContainsKey(resource.ExternalReference) ||
                string.IsNullOrWhiteSpace(resource.ExternalReference) ||
                resource.ExternalReference.Length > 200)
            {
                continue;
            }

            var mapping = await CreateMappingAsync(
                connection,
                resource,
                completedAt,
                cancellationToken);
            if (mapping is not null)
            {
                mappings.Add(mapping.ExternalReference, mapping);
            }
        }

        var imported = 0;
        var duplicates = 0;
        var rejected = 0;
        var batchSignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in batch.Transactions)
        {
            if (!IsValid(item) ||
                !mappings.TryGetValue(item.AccountExternalReference, out var target))
            {
                context.ImportedRecords.Add(new ImportedRecord(
                    job,
                    item.RawPayload,
                    ImportedRecordOutcome.Rejected,
                    item.Amount is > 0 ? item.Amount : null,
                    item.OccurredOn,
                    ValidExternalId(item.ExternalReference),
                    !mappings.ContainsKey(item.AccountExternalReference)
                        ? PluggySynchronizationMessages.AccountNotMapped
                        : PluggySynchronizationMessages.TransactionInvalid));
                rejected++;
                continue;
            }

            var amount = item.Amount!.Value;
            var occurredOn = item.OccurredOn!.Value;
            var externalId = item.ExternalReference;
            var signature = Signature(target, occurredOn, amount, externalId);
            var duplicate = !batchSignatures.Add(signature) || await IsDuplicateAsync(
                target,
                occurredOn,
                amount,
                externalId,
                cancellationToken);
            if (duplicate)
            {
                context.ImportedRecords.Add(new ImportedRecord(
                    job,
                    item.RawPayload,
                    ImportedRecordOutcome.Duplicate,
                    amount,
                    occurredOn,
                    externalId));
                duplicates++;
                continue;
            }

            var category = await ResolveCategoryAsync(
                connection.User,
                item.Category,
                completedAt,
                cancellationToken);
            var record = new ImportedRecord(
                job,
                item.RawPayload,
                ImportedRecordOutcome.Imported,
                amount,
                occurredOn,
                externalId);
            var transaction = target.FinancialAccount is not null
                ? new FinancialTransaction(
                    connection.User,
                    target.FinancialAccount,
                    category,
                    item.Direction!.Value,
                    amount,
                    occurredOn,
                    completedAt,
                    Description(item.Description))
                : new FinancialTransaction(
                    connection.User,
                    target.CreditCard!,
                    category,
                    item.Direction!.Value,
                    amount,
                    occurredOn,
                    completedAt,
                    Description(item.Description));
            transaction.MarkAsImported(record, TransactionSourceType.Pluggy, completedAt);
            if (target.CreditCard is not null)
            {
                await AssignToStatementAsync(
                    transaction,
                    target.CreditCard,
                    completedAt,
                    cancellationToken);
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
        bool requiresReauthentication,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        var job = await context.ImportJobs
            .Include(item => item.Connection)
            .SingleAsync(item => item.PublicId == importJobId, cancellationToken);
        if (job.Status is ImportJobStatus.Pending or ImportJobStatus.Running)
        {
            job.Fail(reason, failedAt);
        }

        if (requiresReauthentication && job.Connection is not null &&
            job.Connection.Status != ConnectionStatus.RequiresReauthentication)
        {
            job.Connection.MarkRequiresReauthentication(failedAt);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<ConnectionResource?> CreateMappingAsync(
        Connection connection,
        PluggyResourceRecord resource,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var currency = await context.Currencies.SingleOrDefaultAsync(
            item => item.Code == resource.CurrencyCode.Trim().ToUpperInvariant(),
            cancellationToken);
        if (currency is null)
        {
            return null;
        }

        var name = await UniqueNameAsync(
            connection.UserId,
            resource.Name,
            resource.Kind,
            cancellationToken);
        ConnectionResource mapping;
        if (resource.Kind == PluggyResourceKind.CreditCard)
        {
            var card = new CreditCard(
                connection.User,
                name,
                SafeName(resource.Institution, "Pluggy")!,
                currency,
                resource.CreditLimit is > 0 ? resource.CreditLimit.Value : 0.01m,
                ValidDay(resource.ClosingDay),
                ValidDay(resource.DueDay),
                ValidLastFour(resource.LastFourDigits),
                createdAt);
            context.CreditCards.Add(card);
            mapping = new ConnectionResource(connection, resource.ExternalReference, card: card);
        }
        else
        {
            var account = new FinancialAccount(
                connection.User,
                name,
                SafeName(resource.Institution, null),
                FinancialAccountType.Checking,
                currency,
                resource.Balance,
                createdAt);
            context.FinancialAccounts.Add(account);
            mapping = new ConnectionResource(connection, resource.ExternalReference, account);
        }

        context.ConnectionResources.Add(mapping);
        return mapping;
    }

    private async Task<string> UniqueNameAsync(
        long userId,
        string requested,
        PluggyResourceKind kind,
        CancellationToken cancellationToken)
    {
        var root = SafeName(requested, kind == PluggyResourceKind.CreditCard
            ? "Imported card"
            : "Imported account")!;
        for (var suffix = 1; ; suffix++)
        {
            var candidate = suffix == 1 ? root : $"{root} ({suffix})";
            if (candidate.Length > 200)
            {
                candidate = $"{root[..Math.Min(root.Length, 190)]} ({suffix})";
            }

            var normalized = candidate.ToUpperInvariant();
            var exists = kind == PluggyResourceKind.CreditCard
                ? await context.CreditCards.AnyAsync(item =>
                    item.UserId == userId && item.NormalizedName == normalized && !item.IsDeleted,
                    cancellationToken)
                : await context.FinancialAccounts.AnyAsync(item =>
                    item.UserId == userId && item.NormalizedName == normalized && !item.IsDeleted,
                    cancellationToken);
            var localExists = kind == PluggyResourceKind.CreditCard
                ? context.CreditCards.Local.Any(item =>
                    item.UserId == userId && item.NormalizedName == normalized && !item.IsDeleted)
                : context.FinancialAccounts.Local.Any(item =>
                    item.UserId == userId && item.NormalizedName == normalized && !item.IsDeleted);
            if (!exists && !localExists)
            {
                return candidate;
            }
        }
    }

    private async Task<Category> ResolveCategoryAsync(
        UserProfile user,
        string? requested,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var name = SafeName(requested, "Imported")!;
        var normalized = name.ToUpperInvariant();
        var local = context.Categories.Local.SingleOrDefault(item =>
            item.UserId == user.Id && item.ParentId == null &&
            item.NormalizedName == normalized && !item.IsDeleted);
        if (local is not null)
        {
            return local;
        }

        var existing = await context.Categories
            .Include(item => item.User)
            .SingleOrDefaultAsync(item =>
                item.UserId == user.Id && item.ParentId == null &&
                item.NormalizedName == normalized && !item.IsDeleted,
                cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var category = new Category(user, name, createdAt);
        context.Categories.Add(category);
        return category;
    }

    private async Task<bool> IsDuplicateAsync(
        ConnectionResource target,
        DateOnly occurredOn,
        decimal amount,
        string? externalId,
        CancellationToken cancellationToken)
    {
        var matches = context.FinancialTransactions.AsNoTracking().Where(item =>
            !item.IsDeleted &&
            item.OccurredOn == occurredOn &&
            item.Amount == amount &&
            item.FinancialAccountId == target.FinancialAccountId &&
            item.CreditCardId == target.CreditCardId);
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
            item.CreditCardId == card.Id &&
            item.PeriodStart == cycle.PeriodStart &&
            item.PeriodEnd == cycle.PeriodEnd &&
            !item.IsDeleted,
            cancellationToken);
        statement ??= context.CreditCardStatements.Local.SingleOrDefault(item =>
            item.CreditCard == card &&
            item.PeriodStart == cycle.PeriodStart && item.PeriodEnd == cycle.PeriodEnd);
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

    private static bool IsValid(PluggyTransactionRecord item) =>
        !string.IsNullOrWhiteSpace(item.RawPayload) &&
        !string.IsNullOrWhiteSpace(item.AccountExternalReference) &&
        (string.IsNullOrWhiteSpace(item.ExternalReference) || item.ExternalReference.Length <= 200) &&
        item.Direction.HasValue &&
        item.Amount is > 0 &&
        item.OccurredOn.HasValue;

    private static string? ValidExternalId(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 200 ? null : value;

    private static string Signature(
        ConnectionResource target,
        DateOnly date,
        decimal amount,
        string? externalId) =>
        $"{target.ExternalReference}|{date:O}|{amount}|{externalId}";

    private static string? Description(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim()[..Math.Min(value.Trim().Length, 500)];

    private static string? SafeName(string? value, string? fallback)
    {
        var name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return name?[..Math.Min(name.Length, 200)];
    }

    private static short ValidDay(short? value) => value is >= 1 and <= 31 ? value.Value : (short)1;

    private static string? ValidLastFour(string? value) =>
        value is { Length: 4 } && value.All(char.IsAsciiDigit) ? value : null;

    private static QueueSynchronizationResult Result(QueueSynchronizationOutcome outcome) =>
        new(null, null, outcome);

    private static ImportJobSnapshot Snapshot(ImportJob job, Guid connectionId) => new(
        job.PublicId,
        connectionId,
        job.SourceType,
        job.Status,
        job.PeriodStart,
        job.PeriodEnd,
        job.ImportedCount,
        job.DuplicateCount,
        job.RejectedCount,
        job.FailureReason,
        job.CreatedAt,
        job.UpdatedAt);
}
