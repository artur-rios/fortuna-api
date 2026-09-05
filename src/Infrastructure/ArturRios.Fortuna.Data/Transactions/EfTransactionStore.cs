using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Transactions;

public sealed class EfTransactionStore(AppDbContext context) : ITransactionStore, ITransactionReader
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
            .Where(transaction =>
                !transaction.IsDeleted &&
                !context.Transfers.Any(transfer =>
                    transfer.OutboundTransactionId == transaction.Id ||
                    transfer.InboundTransactionId == transaction.Id))
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

            exchangeRate = await context.ExchangeRates
                .Include(rate => rate.BaseCurrency)
                .Include(rate => rate.QuoteCurrency)
                .Where(rate =>
                    rate.BaseCurrency.Code == sourceCode &&
                    rate.QuoteCurrency.Code == targetCurrency.Code &&
                    rate.RateDate <= record.OccurredOn)
                .OrderByDescending(rate => rate.RateDate)
                .ThenByDescending(rate => rate.Source)
                .FirstOrDefaultAsync(cancellationToken);
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

        var counterparty = await ResolveCounterpartyAsync(
            user,
            record.Counterparty,
            record.CreatedAt,
            cancellationToken);
        var tags = await ResolveTagsAsync(
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

        if (card is not null)
        {
            await AssignToStatementAsync(
                transaction,
                card,
                record.CreatedAt,
                cancellationToken);
        }

        context.FinancialTransactions.Add(transaction);
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

    private IQueryable<FinancialTransaction> Filter(TransactionSearchCriteria criteria)
    {
        var transactions = context.FinancialTransactions
            .AsNoTracking()
            .Where(transaction => transaction.User.PublicId == criteria.UserId);
        if (!criteria.IncludeDeleted)
        {
            transactions = transactions.Where(transaction => !transaction.IsDeleted);
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
                tag.PublicId == criteria.TagId.Value));
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
            IsTransfer = context.Transfers.Any(transfer =>
                transfer.OutboundTransactionId == transaction.Id ||
                transfer.InboundTransactionId == transaction.Id),
            StatementId = transaction.Statement == null
                ? null
                : transaction.Statement.PublicId,
            IsLateArriving = transaction.IsLateArriving,
            Tags = transaction.Tags
                .OrderBy(tag => tag.Name)
                .ThenBy(tag => tag.PublicId)
                .Select(tag => new TransactionReadTagSnapshot(tag.PublicId, tag.Name))
                .ToArray(),
            IsDeleted = transaction.IsDeleted,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt
        });

    private async Task<Counterparty?> ResolveCounterpartyAsync(
        UserProfile user,
        string? name,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalizedName = name.Trim().ToUpperInvariant();
        var counterparty = await context.Counterparties.SingleOrDefaultAsync(item =>
            item.UserId == user.Id &&
            item.NormalizedName == normalizedName &&
            !item.IsDeleted,
            cancellationToken);
        if (counterparty is not null)
        {
            return counterparty;
        }

        counterparty = new Counterparty(user, name, createdAt);
        context.Counterparties.Add(counterparty);
        return counterparty;
    }

    private async Task<IReadOnlyCollection<Tag>> ResolveTagsAsync(
        UserProfile user,
        IReadOnlyCollection<string> names,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var requested = names
            .Select(name => new { Name = name.Trim(), Normalized = name.Trim().ToUpperInvariant() })
            .DistinctBy(item => item.Normalized)
            .ToArray();
        if (requested.Length == 0)
        {
            return [];
        }

        var normalizedNames = requested.Select(item => item.Normalized).ToArray();
        var existing = await context.Tags.Where(item =>
            item.UserId == user.Id &&
            normalizedNames.Contains(item.NormalizedName) &&
            !item.IsDeleted).ToListAsync(cancellationToken);
        var byName = existing.ToDictionary(item => item.NormalizedName);
        var tags = new List<Tag>(requested.Length);
        foreach (var requestedTag in requested)
        {
            if (!byName.TryGetValue(requestedTag.Normalized, out var tag))
            {
                tag = new Tag(user, requestedTag.Name, createdAt);
                context.Tags.Add(tag);
                byName.Add(requestedTag.Normalized, tag);
            }

            tags.Add(tag);
        }

        return tags;
    }

    private async Task AssignToStatementAsync(
        FinancialTransaction transaction,
        CreditCard card,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var statements = await context.CreditCardStatements
            .Where(item => item.CreditCardId == card.Id && !item.IsDeleted)
            .OrderBy(item => item.PeriodStart)
            .ToListAsync(cancellationToken);
        var intendedCycle = BillingCycle.Containing(
            transaction.OccurredOn,
            card.ClosingDay,
            card.DueDay);
        var statement = statements.SingleOrDefault(item =>
            item.PeriodStart == intendedCycle.PeriodStart &&
            item.PeriodEnd == intendedCycle.PeriodEnd);
        var isLateArriving = statement?.Status == CreditCardStatementStatus.Settled;

        if (isLateArriving)
        {
            var cycle = intendedCycle.Next(card.ClosingDay, card.DueDay);
            while (true)
            {
                statement = statements.SingleOrDefault(item =>
                    item.PeriodStart == cycle.PeriodStart &&
                    item.PeriodEnd == cycle.PeriodEnd);
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

    private static TransactionRecordResult Result(
        TransactionRecordOutcome outcome,
        TransactionSnapshot? transaction = null) => new(transaction, outcome);
}
