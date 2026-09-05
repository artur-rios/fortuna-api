using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Transactions;

public sealed class EfRecurringTransactionStore(AppDbContext context, TimeProvider timeProvider)
    : IRecurringTransactionStore, IRecurringTransactionReader
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

    private static RecurringTransactionSnapshot Snapshot(RecurringTransaction rule, DateOnly previewFrom) => new()
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
        NextOccurrences = rule.NextOccurrences(previewFrom),
        CreatedAt = rule.CreatedAt,
        UpdatedAt = rule.UpdatedAt
    };

    private static RecurringTransactionRecordResult Result(
        RecurringTransactionRecordOutcome outcome,
        RecurringTransactionSnapshot? rule = null) => new(rule, outcome);
}
