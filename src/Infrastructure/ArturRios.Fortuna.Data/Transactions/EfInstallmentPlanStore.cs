using ArturRios.Fortuna.Data.Cards;
using ArturRios.Fortuna.Data.Classification;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Currencies;
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

            exchangeRate = await ExchangeRateLookup.FindLatestAsync(
                context,
                sourceCode,
                card.Currency.Code,
                record.PurchasedOn,
                cancellationToken);
            if (exchangeRate is null)
            {
                return Result(InstallmentPlanRecordOutcome.ExchangeRateUnavailable);
            }

            billedTotal = decimal.Round(
                record.TotalAmount * exchangeRate.Rate,
                card.Currency.MinorUnitDigits,
                MidpointRounding.AwayFromZero);
        }

        var billedSplit = InstallmentPlan.TrySplit(
            billedTotal,
            record.InstallmentCount,
            card.Currency.MinorUnitDigits);
        var originalSplit = originalCurrency is null
            ? null
            : InstallmentPlan.TrySplit(
                record.TotalAmount,
                record.InstallmentCount,
                originalCurrency.MinorUnitDigits);
        if (!billedSplit.Succeeded || originalSplit is { Succeeded: false })
        {
            return Result(InstallmentPlanRecordOutcome.AmountTooSmall);
        }

        var billedAmounts = billedSplit.Amounts;
        var originalAmounts = originalSplit?.Amounts;

        var counterparty = await ClassificationResolver.GetOrCreateCounterpartyAsync(
            context,
            card.User,
            record.Counterparty,
            record.CreatedAt,
            cancellationToken);
        // The split brings the total to the card currency's minor unit; the plan records that
        // total so it always equals the sum of its installments (BR-10).
        var plan = new InstallmentPlan(
            card,
            billedAmounts.Sum(),
            record.InstallmentCount,
            record.PurchasedOn,
            record.CreatedAt);

        // FR-TX-20: each installment goes to the cycle after the previous one. The same day of
        // the following month is not enough: AddMonths clamps a 29th–31st to a short month,
        // which can land back in the previous installment's cycle (closing day 28, purchase on
        // 30 Jan: 28 Feb closes the same cycle as 30 Jan), so the date is kept inside its cycle.
        var cycle = BillingCycle.Containing(record.PurchasedOn, card.ClosingDay, card.DueDay);
        for (short index = 0; index < record.InstallmentCount; index++)
        {
            if (index > 0)
            {
                cycle = cycle.Next(card.ClosingDay, card.DueDay);
            }

            var sameDay = record.PurchasedOn.AddMonths(index);
            var occurredOn = sameDay < cycle.PeriodStart ? cycle.PeriodStart
                : sameDay > cycle.PeriodEnd ? cycle.PeriodEnd
                : sameDay;
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
            await CreditCardStatementResolver.AssignAsync(
                context,
                transaction,
                card,
                record.CreatedAt,
                cancellationToken);
        }

        context.InstallmentPlans.Add(plan);
        await CreditCardStatementResolver.RefreshTotalsAsync(
            context,
            plan.Installments.Select(installment => installment.Statement),
            record.CreatedAt,
            cancellationToken);
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
