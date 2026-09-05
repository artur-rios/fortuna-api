using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Cards;
using ArturRios.Fortuna.Domain.Classification;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Transactions;

public sealed class EfInstallmentPlanStore(
    AppDbContext context,
    ITransactionLifecycleStore transactionLifecycle)
    : IInstallmentPlanStore, IInstallmentPlanReader, IInstallmentPlanLifecycleStore
{
    public async Task<InstallmentPlanRecordResult> RecordAsync(
        InstallmentPlanRecord record,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var card = await context.CreditCards
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleOrDefaultAsync(item =>
                item.PublicId == record.CreditCardId &&
                item.User.PublicId == record.UserId &&
                !item.IsDeleted,
                cancellationToken);
        if (card is null)
        {
            return Result(InstallmentPlanRecordOutcome.CreditCardNotFound);
        }

        var category = await context.Categories.SingleOrDefaultAsync(item =>
            item.PublicId == record.CategoryId &&
            item.UserId == card.UserId &&
            !item.IsDeleted,
            cancellationToken);
        if (category is null)
        {
            return Result(InstallmentPlanRecordOutcome.CategoryNotFound);
        }

        var sourceCode = string.IsNullOrWhiteSpace(record.CurrencyCode)
            ? card.Currency.Code
            : record.CurrencyCode.Trim().ToUpperInvariant();
        Currency? originalCurrency = null;
        ExchangeRate? exchangeRate = null;
        var billedTotal = record.TotalAmount;
        if (sourceCode != card.Currency.Code)
        {
            originalCurrency = await context.Currencies.SingleOrDefaultAsync(
                item => item.Code == sourceCode,
                cancellationToken);
            if (originalCurrency is null)
            {
                return Result(InstallmentPlanRecordOutcome.CurrencyNotSupported);
            }

            exchangeRate = await context.ExchangeRates
                .Include(rate => rate.BaseCurrency)
                .Include(rate => rate.QuoteCurrency)
                .Where(rate =>
                    rate.BaseCurrency.Code == sourceCode &&
                    rate.QuoteCurrency.Code == card.Currency.Code &&
                    rate.RateDate <= record.PurchasedOn)
                .OrderByDescending(rate => rate.RateDate)
                .ThenByDescending(rate => rate.Source)
                .FirstOrDefaultAsync(cancellationToken);
            if (exchangeRate is null)
            {
                return Result(InstallmentPlanRecordOutcome.ExchangeRateUnavailable);
            }

            billedTotal = decimal.Round(
                record.TotalAmount * exchangeRate.Rate,
                card.Currency.MinorUnitDigits,
                MidpointRounding.AwayFromZero);
        }

        IReadOnlyList<decimal> billedAmounts;
        IReadOnlyList<decimal>? originalAmounts = null;
        try
        {
            billedAmounts = InstallmentPlan.Split(
                billedTotal,
                record.InstallmentCount,
                card.Currency.MinorUnitDigits);
            if (originalCurrency is not null)
            {
                originalAmounts = InstallmentPlan.Split(
                    record.TotalAmount,
                    record.InstallmentCount,
                    originalCurrency.MinorUnitDigits);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result(InstallmentPlanRecordOutcome.AmountTooSmall);
        }

        var counterparty = await ResolveCounterpartyAsync(
            card.User,
            record.Counterparty,
            record.CreatedAt,
            cancellationToken);
        var statements = await context.CreditCardStatements
            .Where(item => item.CreditCardId == card.Id && !item.IsDeleted)
            .OrderBy(item => item.PeriodStart)
            .ToListAsync(cancellationToken);
        var plan = new InstallmentPlan(
            card,
            billedTotal,
            record.InstallmentCount,
            record.PurchasedOn,
            record.CreatedAt);

        for (short index = 0; index < record.InstallmentCount; index++)
        {
            var occurredOn = record.PurchasedOn.AddMonths(index);
            var transaction = new FinancialTransaction(
                card.User,
                card,
                category,
                TransactionDirection.Expense,
                billedAmounts[index],
                occurredOn,
                record.CreatedAt,
                counterparty: counterparty);
            if (exchangeRate is not null)
            {
                transaction.RecordForeignCurrencyDetails(
                    originalAmounts![index],
                    originalCurrency!,
                    exchangeRate.Rate,
                    exchangeRate.RateDate,
                    record.CreatedAt);
            }

            plan.AddInstallment(transaction, (short)(index + 1), record.CreatedAt);
            AssignToStatement(transaction, card, statements, record.CreatedAt);
        }

        context.InstallmentPlans.Add(plan);
        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);
        return Result(InstallmentPlanRecordOutcome.Succeeded, Snapshot(plan));
    }

    public async Task<InstallmentPlanSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var query = context.InstallmentPlans
            .AsNoTracking()
            .Include(plan => plan.CreditCard)
                .ThenInclude(card => card.Currency)
            .Include(plan => plan.Installments)
                .ThenInclude(transaction => transaction.Currency)
            .Include(plan => plan.Installments)
                .ThenInclude(transaction => transaction.OriginalCurrency)
            .Include(plan => plan.Installments)
                .ThenInclude(transaction => transaction.Statement)
            .Where(plan =>
                plan.PublicId == id &&
                plan.CreditCard.User.PublicId == userId);
        if (!includeDeleted)
        {
            query = query.Where(plan => !plan.IsDeleted);
        }

        var plan = await query.SingleOrDefaultAsync(cancellationToken);
        return plan is null ? null : Snapshot(plan);
    }

    public Task<InstallmentPlanLifecycleResult> SoftDeleteAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken) => ChangeLifecycleAsync(
        userId,
        id,
        (transactionId, token) => transactionLifecycle.SoftDeleteAsync(
            userId,
            transactionId,
            changedAt,
            token),
        cancellationToken);

    public Task<InstallmentPlanLifecycleResult> RestoreAsync(
        Guid userId,
        Guid id,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken) => ChangeLifecycleAsync(
        userId,
        id,
        (transactionId, token) => transactionLifecycle.RestoreAsync(
            userId,
            transactionId,
            changedAt,
            token),
        cancellationToken);

    private async Task<InstallmentPlanLifecycleResult> ChangeLifecycleAsync(
        Guid userId,
        Guid id,
        Func<Guid, CancellationToken, Task<TransactionLifecycleResult>> change,
        CancellationToken cancellationToken)
    {
        var plan = await context.InstallmentPlans
            .AsNoTracking()
            .Where(item =>
                item.PublicId == id &&
                item.CreditCard.User.PublicId == userId)
            .Select(item => new
            {
                item.PublicId,
                TransactionId = item.Installments
                    .OrderBy(transaction => transaction.InstallmentNumber)
                    .Select(transaction => transaction.PublicId)
                    .First()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (plan is null)
        {
            return LifecycleResult(InstallmentPlanLifecycleOutcome.NotFound);
        }

        var result = await change(plan.TransactionId, cancellationToken);
        return result.Outcome switch
        {
            TransactionLifecycleOutcome.Succeeded => LifecycleResult(
                InstallmentPlanLifecycleOutcome.Succeeded,
                plan.PublicId),
            TransactionLifecycleOutcome.NotFound => LifecycleResult(
                InstallmentPlanLifecycleOutcome.NotFound),
            TransactionLifecycleOutcome.RestoreRequiresSoftDeletion => LifecycleResult(
                InstallmentPlanLifecycleOutcome.RestoreRequiresSoftDeletion),
            TransactionLifecycleOutcome.SettledStatementFrozen => LifecycleResult(
                InstallmentPlanLifecycleOutcome.SettledStatementFrozen),
            _ => throw new InvalidOperationException(
                "The delegated transaction lifecycle returned an unsupported outcome.")
        };
    }

    private static void AssignToStatement(
        FinancialTransaction transaction,
        CreditCard card,
        ICollection<CreditCardStatement> statements,
        DateTimeOffset changedAt)
    {
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
                    statements.Add(statement);
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
            statements.Add(statement);
        }

        transaction.AssignToStatement(statement, isLateArriving, changedAt);
        statement.RecalculatePurchaseTotal(
            statement.PurchaseTotal + transaction.Amount,
            changedAt);
    }

    private async Task<Counterparty?> ResolveCounterpartyAsync(
        ArturRios.Fortuna.Domain.Users.UserProfile user,
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

    private static InstallmentPlanSnapshot Snapshot(InstallmentPlan plan)
    {
        var installments = plan.Installments
            .OrderBy(item => item.InstallmentNumber)
            .Select(item => new InstallmentSnapshot(
                item.PublicId,
                item.InstallmentNumber!.Value,
                item.Amount,
                item.Currency.Code,
                item.OriginalAmount,
                item.OriginalCurrency?.Code,
                item.AppliedRate,
                item.RateDate,
                item.OccurredOn,
                item.Statement?.PublicId,
                item.IsLateArriving,
                item.IsDeleted))
            .ToArray();
        var first = installments[0];
        return new InstallmentPlanSnapshot
        {
            Id = plan.PublicId,
            CreditCardId = plan.CreditCard.PublicId,
            TotalAmount = plan.TotalAmount,
            CurrencyCode = plan.CreditCard.Currency.Code,
            OriginalTotalAmount = installments.Any(item => item.OriginalAmount.HasValue)
                ? installments.Sum(item => item.OriginalAmount!.Value)
                : null,
            OriginalCurrencyCode = first.OriginalCurrencyCode,
            AppliedRate = first.AppliedRate,
            RateDate = first.RateDate,
            InstallmentCount = plan.InstallmentCount,
            PurchasedOn = plan.PurchasedOn,
            IsDeleted = plan.IsDeleted,
            CreatedAt = plan.CreatedAt,
            UpdatedAt = plan.UpdatedAt,
            Installments = installments
        };
    }

    private static InstallmentPlanRecordResult Result(
        InstallmentPlanRecordOutcome outcome,
        InstallmentPlanSnapshot? plan = null) => new(plan, outcome);

    private static InstallmentPlanLifecycleResult LifecycleResult(
        InstallmentPlanLifecycleOutcome outcome,
        Guid? id = null) => new(id, outcome);
}
