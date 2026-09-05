using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Transactions;
using ArturRios.Fortuna.Domain.Accounts;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Investments;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Investments;

public sealed class EfInvestmentMovementStore(AppDbContext context) : IInvestmentMovementStore
{
    public async Task<InvestmentMovementRecordResult> RecordAsync(
        InvestmentMovementRecord record,
        CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var investment = await context.Investments
            .Include(item => item.User)
            .Include(item => item.Currency)
            .SingleOrDefaultAsync(item =>
                item.PublicId == record.InvestmentId &&
                item.User.PublicId == record.UserId &&
                !item.IsDeleted,
                cancellationToken);
        if (investment is null)
        {
            return Result(InvestmentMovementRecordOutcome.InvestmentNotFound);
        }

        FinancialAccount? account = null;
        ExchangeRate? exchangeRate = null;
        var movementAmount = record.Amount;
        if (record.FinancialAccountId.HasValue)
        {
            account = await context.FinancialAccounts
                .Include(item => item.User)
                .Include(item => item.Currency)
                .SingleOrDefaultAsync(item =>
                    item.PublicId == record.FinancialAccountId.Value &&
                    item.User.PublicId == record.UserId &&
                    !item.IsDeleted,
                    cancellationToken);
            if (account is null)
            {
                return Result(InvestmentMovementRecordOutcome.FinancialAccountNotFound);
            }

            if (account.Currency.Code != investment.Currency.Code)
            {
                exchangeRate = await context.ExchangeRates
                    .Include(rate => rate.BaseCurrency)
                    .Include(rate => rate.QuoteCurrency)
                    .Where(rate =>
                        rate.BaseCurrency.Code == account.Currency.Code &&
                        rate.QuoteCurrency.Code == investment.Currency.Code &&
                        rate.RateDate <= record.OccurredOn)
                    .OrderByDescending(rate => rate.RateDate)
                    .ThenByDescending(rate => rate.Source)
                    .FirstOrDefaultAsync(cancellationToken);
                if (exchangeRate is null)
                {
                    return Result(InvestmentMovementRecordOutcome.ExchangeRateUnavailable);
                }

                movementAmount = decimal.Round(
                    record.Amount * exchangeRate.Rate,
                    investment.Currency.MinorUnitDigits,
                    MidpointRounding.AwayFromZero);
                if (movementAmount <= 0m)
                {
                    return Result(InvestmentMovementRecordOutcome.ConvertedAmountTooSmall);
                }
            }
        }

        var movement = new InvestmentMovement(
            investment,
            record.MovementType,
            movementAmount,
            record.OccurredOn,
            record.CreatedAt);
        context.InvestmentMovements.Add(movement);

        FinancialTransaction? outbound = null;
        Transfer? transfer = null;
        if (account is not null)
        {
            var transferCategory = await TransactionCategoryResolver.GetOrCreateAsync(
                context,
                account.User,
                TransactionCategoryResolver.Transfers,
                record.CreatedAt,
                cancellationToken);
            outbound = new FinancialTransaction(
                account.User,
                account,
                transferCategory,
                TransactionDirection.Expense,
                record.Amount,
                record.OccurredOn,
                record.CreatedAt);
            transfer = new Transfer(
                outbound,
                movement,
                exchangeRate?.Rate,
                exchangeRate?.RateDate,
                record.CreatedAt);
            context.FinancialTransactions.Add(outbound);
            context.Transfers.Add(transfer);
        }

        await context.SaveChangesAsync(cancellationToken);
        var position = await context.InvestmentMovements
            .Where(item => item.InvestmentId == investment.Id && !item.IsDeleted)
            .Select(item => (decimal?)(
                item.MovementType == InvestmentMovementType.Contribution ||
                item.MovementType == InvestmentMovementType.Yield
                    ? item.Amount
                    : -item.Amount))
            .SumAsync(cancellationToken) ?? 0m;
        await databaseTransaction.CommitAsync(cancellationToken);

        return Result(
            InvestmentMovementRecordOutcome.Succeeded,
            new InvestmentMovementSnapshot(
                movement.PublicId,
                investment.PublicId,
                movement.MovementType,
                movement.Amount,
                investment.Currency.Code,
                movement.OccurredOn,
                position,
                account?.PublicId,
                account is null ? null : record.Amount,
                account?.Currency.Code,
                transfer?.PublicId,
                outbound?.PublicId,
                exchangeRate?.Rate,
                exchangeRate?.RateDate,
                movement.CreatedAt,
                movement.UpdatedAt));
    }

    private static InvestmentMovementRecordResult Result(
        InvestmentMovementRecordOutcome outcome,
        InvestmentMovementSnapshot? movement = null) => new(movement, outcome);
}
