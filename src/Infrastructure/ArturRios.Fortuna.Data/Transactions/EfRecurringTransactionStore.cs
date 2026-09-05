using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArturRios.Fortuna.Data.Transactions;

public sealed class EfRecurringTransactionStore(
    AppDbContext context,
    TimeProvider timeProvider,
    ILogger<EfRecurringTransactionStore> logger)
    : IRecurringTransactionStore, IRecurringTransactionReader, IRecurringTransactionUpdater,
        IRecurringTransactionLifecycleStore, IRecurringTransactionMaterializer
{
    public async Task<RecurringTransactionRecordResult> RecordAsync(
        RecurringTransactionRecord record,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var account = record.FinancialAccountId.HasValue
            ? await context.FinancialAccounts.Include(item => item.User).Include(item => item.Currency)
                .SingleOrDefaultAsync(item => item.PublicId == record.FinancialAccountId &&
                    item.User.PublicId == record.UserId && !item.IsDeleted, cancellationToken)
            : null;
        if (record.FinancialAccountId.HasValue && account is null)
        {
            return Result(RecurringTransactionRecordOutcome.FinancialAccountNotFound);
        }

        var card = record.CreditCardId.HasValue
            ? await context.CreditCards.Include(item => item.User).Include(item => item.Currency)
                .SingleOrDefaultAsync(item => item.PublicId == record.CreditCardId &&
                    item.User.PublicId == record.UserId && !item.IsDeleted, cancellationToken)
            : null;
        if (record.CreditCardId.HasValue && card is null)
        {
            return Result(RecurringTransactionRecordOutcome.CreditCardNotFound);
        }

        var user = account?.User ?? card!.User;
        var category = await context.Categories.SingleOrDefaultAsync(item =>
            item.PublicId == record.CategoryId && item.UserId == user.Id && !item.IsDeleted,
            cancellationToken);
        if (category is null)
        {
            return Result(RecurringTransactionRecordOutcome.CategoryNotFound);
        }

        var counterparty = await ResolveCounterpartyAsync(user, record.Counterparty, record.CreatedAt, cancellationToken);
        var rule = new RecurringTransaction(
            user, account, card, category, record.Direction, record.Amount, record.Frequency,
            record.StartsOn, record.EndsOn, record.CreatedAt, record.Description, counterparty);
        context.RecurringTransactions.Add(rule);
        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);
        return Result(RecurringTransactionRecordOutcome.Succeeded, Snapshot(rule, record.PreviewFrom));
    }

    public async Task<RecurringTransactionSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var rule = await context.RecurringTransactions.AsNoTracking()
            .Include(item => item.FinancialAccount).Include(item => item.CreditCard)
            .Include(item => item.Category).Include(item => item.Counterparty).Include(item => item.Currency)
            .SingleOrDefaultAsync(item => item.PublicId == id && item.User.PublicId == userId && !item.IsDeleted,
                cancellationToken);
        return rule is null
            ? null
            : Snapshot(rule, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
    }

    public async Task<RecurringTransactionUpdateResult> UpdateAsync(
        RecurringTransactionUpdate update,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var rule = await FindRuleAsync(update.UserId, update.Id, cancellationToken);
        if (rule is null)
        {
            return UpdateResult(RecurringTransactionUpdateOutcome.NotFound);
        }

        var account = update.FinancialAccountId.HasValue
            ? await context.FinancialAccounts.Include(item => item.User).Include(item => item.Currency)
                .SingleOrDefaultAsync(item =>
                    item.PublicId == update.FinancialAccountId &&
                    item.User.PublicId == update.UserId &&
                    !item.IsDeleted,
                    cancellationToken)
            : null;
        if (update.FinancialAccountId.HasValue && account is null)
        {
            return UpdateResult(RecurringTransactionUpdateOutcome.FinancialAccountNotFound);
        }

        var card = update.CreditCardId.HasValue
            ? await context.CreditCards.Include(item => item.User).Include(item => item.Currency)
                .SingleOrDefaultAsync(item =>
                    item.PublicId == update.CreditCardId &&
                    item.User.PublicId == update.UserId &&
                    !item.IsDeleted,
                    cancellationToken)
            : null;
        if (update.CreditCardId.HasValue && card is null)
        {
            return UpdateResult(RecurringTransactionUpdateOutcome.CreditCardNotFound);
        }

        var category = await context.Categories.SingleOrDefaultAsync(item =>
            item.PublicId == update.CategoryId && item.UserId == rule.UserId && !item.IsDeleted,
            cancellationToken);
        if (category is null)
        {
            return UpdateResult(RecurringTransactionUpdateOutcome.CategoryNotFound);
        }

        var counterparty = await ResolveCounterpartyAsync(
            rule.User, update.Counterparty, update.UpdatedAt, cancellationToken);
        rule.UpdateTemplate(
            account, card, category, update.Direction, update.Amount, update.Frequency,
            update.StartsOn, update.EndsOn, update.Description, counterparty, update.UpdatedAt);
        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);
        return UpdateResult(
            RecurringTransactionUpdateOutcome.Succeeded,
            Snapshot(rule, update.PreviewFrom));
    }

    public async Task<RecurringTransactionLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var rule = await context.RecurringTransactions.SingleOrDefaultAsync(item =>
            item.PublicId == id && item.User.PublicId == userId && !item.IsDeleted,
            cancellationToken);
        if (rule is null)
        {
            return new RecurringTransactionLifecycleResult(
                null, RecurringTransactionLifecycleOutcome.NotFound);
        }

        rule.SoftDelete(changedAt);
        await context.SaveChangesAsync(cancellationToken);
        return new RecurringTransactionLifecycleResult(
            rule.PublicId, RecurringTransactionLifecycleOutcome.Succeeded);
    }

    public async Task<RecurringMaterializationResult> MaterializeAsync(
        RecurringMaterializationRun run,
        CancellationToken cancellationToken)
    {
        var ruleIds = await context.RecurringTransactions.AsNoTracking()
            .Where(rule => rule.User.PublicId == run.UserId && !rule.IsDeleted && rule.StartsOn <= run.Through)
            .OrderBy(rule => rule.CreatedAt)
            .ThenBy(rule => rule.PublicId)
            .Select(rule => rule.PublicId)
            .ToArrayAsync(cancellationToken);
        var results = new List<RecurringRuleMaterializationResult>(ruleIds.Length);
        foreach (var ruleId in ruleIds)
        {
            results.Add(await MaterializeRuleAsync(run, ruleId, cancellationToken));
        }

        return new RecurringMaterializationResult(results);
    }

    private async Task<RecurringRuleMaterializationResult> MaterializeRuleAsync(
        RecurringMaterializationRun run,
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        var rule = await FindRuleAsync(run.UserId, ruleId, cancellationToken)
            ?? throw new InvalidOperationException("A recurring transaction disappeared during materialization.");
        var skipReason = DeletedReference(rule);
        if (skipReason.HasValue)
        {
            return new RecurringRuleMaterializationResult(
                rule.PublicId, [], rule.IsCompleteOn(run.Through), skipReason);
        }

        var firstDueDate = rule.LastMaterializedOn?.AddDays(1) ?? rule.StartsOn;
        var dueDates = rule.OccurrencesBetween(firstDueDate, run.Through);
        var occurrenceResults = new List<RecurringOccurrenceMaterializationResult>(dueDates.Count);
        var markerCanAdvance = true;
        foreach (var dueDate in dueDates)
        {
            context.ChangeTracker.Clear();
            rule = await FindRuleAsync(run.UserId, ruleId, cancellationToken)
                ?? throw new InvalidOperationException("A recurring transaction disappeared during materialization.");
            var existing = await context.FinancialTransactions.AsNoTracking().SingleOrDefaultAsync(transaction =>
                transaction.RecurringTransactionId == rule.Id && transaction.OccurredOn == dueDate,
                cancellationToken);
            if (existing is not null)
            {
                if (markerCanAdvance)
                {
                    rule.MarkMaterializedThrough(dueDate, run.MaterializedAt);
                    await context.SaveChangesAsync(cancellationToken);
                }

                continue;
            }

            await using var databaseTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var possibleDuplicate = await IsPossibleImportedDuplicateAsync(rule, dueDate, cancellationToken);
                var transaction = rule.FinancialAccount is not null
                    ? new FinancialTransaction(
                        rule.User, rule.FinancialAccount, rule.Category, rule.Direction, rule.Amount,
                        dueDate, run.MaterializedAt, rule.Description, rule.Counterparty)
                    : new FinancialTransaction(
                        rule.User, rule.CreditCard!, rule.Category, rule.Direction, rule.Amount,
                        dueDate, run.MaterializedAt, rule.Description, rule.Counterparty);
                transaction.MarkAsRecurringOccurrence(rule, possibleDuplicate, run.MaterializedAt);
                if (rule.CreditCard is not null)
                {
                    await AssignToStatementAsync(
                        transaction, rule.CreditCard, run.MaterializedAt, cancellationToken);
                }

                if (markerCanAdvance)
                {
                    rule.MarkMaterializedThrough(dueDate, run.MaterializedAt);
                }

                context.FinancialTransactions.Add(transaction);
                await context.SaveChangesAsync(cancellationToken);
                await databaseTransaction.CommitAsync(cancellationToken);
                occurrenceResults.Add(new RecurringOccurrenceMaterializationResult(
                    dueDate, transaction.PublicId, possibleDuplicate));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await databaseTransaction.RollbackAsync(cancellationToken);
                logger.LogError(
                    exception,
                    "Failed to materialize recurring transaction {RecurringTransactionId} on {OccurredOn}",
                    ruleId,
                    dueDate);
                occurrenceResults.Add(new RecurringOccurrenceMaterializationResult(
                    dueDate, null, false, RecurringTransactionMessages.OccurrenceFailed));
                markerCanAdvance = false;
            }
        }

        context.ChangeTracker.Clear();
        rule = await FindRuleAsync(run.UserId, ruleId, cancellationToken)
            ?? throw new InvalidOperationException("A recurring transaction disappeared during materialization.");
        return new RecurringRuleMaterializationResult(
            rule.PublicId,
            occurrenceResults,
            rule.IsCompleteOn(run.Through) && occurrenceResults.All(occurrence => occurrence.Error is null));
    }

    private Task<RecurringTransaction?> FindRuleAsync(
        Guid userId,
        Guid ruleId,
        CancellationToken cancellationToken) => context.RecurringTransactions
        .Include(rule => rule.User)
        .Include(rule => rule.FinancialAccount).ThenInclude(account => account!.Currency)
        .Include(rule => rule.CreditCard).ThenInclude(card => card!.Currency)
        .Include(rule => rule.Category)
        .Include(rule => rule.Counterparty)
        .SingleOrDefaultAsync(rule =>
            rule.PublicId == ruleId && rule.User.PublicId == userId && !rule.IsDeleted,
            cancellationToken);

    private static RecurringMaterializationSkipReason? DeletedReference(RecurringTransaction rule)
    {
        if (rule.FinancialAccount?.IsDeleted == true)
        {
            return RecurringMaterializationSkipReason.FinancialAccountDeleted;
        }

        if (rule.CreditCard?.IsDeleted == true)
        {
            return RecurringMaterializationSkipReason.CreditCardDeleted;
        }

        return rule.Category.IsDeleted
            ? RecurringMaterializationSkipReason.CategoryDeleted
            : null;
    }

    private Task<bool> IsPossibleImportedDuplicateAsync(
        RecurringTransaction rule,
        DateOnly occurredOn,
        CancellationToken cancellationToken) => context.FinancialTransactions.AnyAsync(transaction =>
        transaction.UserId == rule.UserId &&
        transaction.FinancialAccountId == rule.FinancialAccountId &&
        transaction.CreditCardId == rule.CreditCardId &&
        transaction.Direction == rule.Direction &&
        transaction.Amount == rule.Amount &&
        transaction.OccurredOn == occurredOn &&
        transaction.SourceType != TransactionSourceType.Manual &&
        !transaction.IsDeleted,
        cancellationToken);

    private async Task AssignToStatementAsync(
        FinancialTransaction transaction,
        CreditCard card,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var statements = await context.CreditCardStatements
            .Where(statement => statement.CreditCardId == card.Id && !statement.IsDeleted)
            .OrderBy(statement => statement.PeriodStart)
            .ToListAsync(cancellationToken);
        var intendedCycle = BillingCycle.Containing(
            transaction.OccurredOn, card.ClosingDay, card.DueDay);
        var statement = statements.SingleOrDefault(item =>
            item.PeriodStart == intendedCycle.PeriodStart && item.PeriodEnd == intendedCycle.PeriodEnd);
        var isLateArriving = statement?.Status == CreditCardStatementStatus.Settled;
        if (isLateArriving)
        {
            var cycle = intendedCycle.Next(card.ClosingDay, card.DueDay);
            while (true)
            {
                statement = statements.SingleOrDefault(item =>
                    item.PeriodStart == cycle.PeriodStart && item.PeriodEnd == cycle.PeriodEnd);
                if (statement is null)
                {
                    statement = new CreditCardStatement(card, cycle, changedAt);
                    context.CreditCardStatements.Add(statement);
                    break;
                }

                if (statement.Status == CreditCardStatementStatus.Open)
                {
                    break;
                }

                cycle = cycle.Next(card.ClosingDay, card.DueDay);
            }
        }
        else if (statement is null)
        {
            statement = new CreditCardStatement(card, intendedCycle, changedAt);
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
        var signedAmount = transaction.Direction == TransactionDirection.Expense
            ? transaction.Amount
            : -transaction.Amount;
        transaction.AssignToStatement(statement, isLateArriving, changedAt);
        statement.RecalculatePurchaseTotal(existingTotal + signedAmount, changedAt);
    }

    private async Task<Counterparty?> ResolveCounterpartyAsync(
        UserProfile user, string? name, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var normalizedName = name.Trim().ToUpperInvariant();
        var existing = await context.Counterparties.SingleOrDefaultAsync(item =>
            item.UserId == user.Id && item.NormalizedName == normalizedName && !item.IsDeleted,
            cancellationToken);
        if (existing is not null) return existing;
        var counterparty = new Counterparty(user, name, createdAt);
        context.Counterparties.Add(counterparty);
        return counterparty;
    }

    private static RecurringTransactionSnapshot Snapshot(RecurringTransaction rule, DateOnly previewFrom)
    {
        var occurrenceFrom = rule.LastMaterializedOn.HasValue &&
            rule.LastMaterializedOn.Value >= previewFrom
                ? rule.LastMaterializedOn.Value.AddDays(1)
                : previewFrom;
        return new RecurringTransactionSnapshot
        {
            Id = rule.PublicId,
            FinancialAccountId = rule.FinancialAccount?.PublicId,
            CreditCardId = rule.CreditCard?.PublicId,
            CategoryId = rule.Category.PublicId,
            Direction = rule.Direction,
            Amount = rule.Amount,
            CurrencyCode = rule.Currency.Code,
            Frequency = rule.Frequency,
            StartsOn = rule.StartsOn,
            EndsOn = rule.EndsOn,
            LastMaterializedOn = rule.LastMaterializedOn,
            Description = rule.Description,
            CounterpartyId = rule.Counterparty?.PublicId,
            CounterpartyName = rule.Counterparty?.Name,
            NextOccurrences = rule.NextOccurrences(occurrenceFrom),
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }

    private static RecurringTransactionRecordResult Result(
        RecurringTransactionRecordOutcome outcome,
        RecurringTransactionSnapshot? rule = null) => new(rule, outcome);

    private static RecurringTransactionUpdateResult UpdateResult(
        RecurringTransactionUpdateOutcome outcome,
        RecurringTransactionSnapshot? rule = null) => new(rule, outcome);
}
