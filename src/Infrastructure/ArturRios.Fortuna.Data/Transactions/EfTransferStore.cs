using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Transactions;

public sealed class EfTransferStore(AppDbContext context) : ITransferStore
{
    public async Task<TransferRecordResult> RecordAsync(
        TransferRecord record,
        CancellationToken cancellationToken)
    {
        if (record.OriginFinancialAccountId == record.DestinationFinancialAccountId)
        {
            return Result(TransferRecordOutcome.AccountsMustDiffer);
        }

        await using var databaseTransaction = await context.Database.BeginTransactionAsync(
            cancellationToken);
        var origin = await context.FinancialAccounts
            .Include(account => account.User)
            .Include(account => account.Currency)
            .SingleOrDefaultAsync(account =>
                account.PublicId == record.OriginFinancialAccountId &&
                account.User.PublicId == record.UserId &&
                !account.IsDeleted,
                cancellationToken);
        if (origin is null)
        {
            return Result(TransferRecordOutcome.OriginFinancialAccountNotFound);
        }

        var destination = await context.FinancialAccounts
            .Include(account => account.User)
            .Include(account => account.Currency)
            .SingleOrDefaultAsync(account =>
                account.PublicId == record.DestinationFinancialAccountId &&
                account.User.PublicId == record.UserId &&
                !account.IsDeleted,
                cancellationToken);
        if (destination is null)
        {
            return Result(TransferRecordOutcome.DestinationFinancialAccountNotFound);
        }

        ExchangeRate? exchangeRate = null;
        var inboundAmount = record.Amount;
        if (origin.Currency.Code != destination.Currency.Code)
        {
            exchangeRate = await context.ExchangeRates
                .Include(rate => rate.BaseCurrency)
                .Include(rate => rate.QuoteCurrency)
                .Where(rate =>
                    rate.BaseCurrency.Code == origin.Currency.Code &&
                    rate.QuoteCurrency.Code == destination.Currency.Code &&
                    rate.RateDate <= record.OccurredOn)
                .OrderByDescending(rate => rate.RateDate)
                .ThenByDescending(rate => rate.Source)
                .FirstOrDefaultAsync(cancellationToken);
            if (exchangeRate is null)
            {
                return Result(TransferRecordOutcome.ExchangeRateUnavailable);
            }

            inboundAmount = decimal.Round(
                record.Amount * exchangeRate.Rate,
                destination.Currency.MinorUnitDigits,
                MidpointRounding.AwayFromZero);
            if (inboundAmount <= 0m)
            {
                return Result(TransferRecordOutcome.ConvertedAmountTooSmall);
            }
        }

        var category = await TransactionCategoryResolver.GetOrCreateAsync(
            context,
            origin.User,
            TransactionCategoryResolver.Transfers,
            record.CreatedAt,
            cancellationToken);
        var outbound = new FinancialTransaction(
            origin.User,
            origin,
            category,
            TransactionDirection.Expense,
            record.Amount,
            record.OccurredOn,
            record.CreatedAt);
        var inbound = new FinancialTransaction(
            destination.User,
            destination,
            category,
            TransactionDirection.Earning,
            inboundAmount,
            record.OccurredOn,
            record.CreatedAt);
        if (exchangeRate is not null)
        {
            inbound.RecordForeignCurrencyDetails(
                record.Amount,
                origin.Currency,
                exchangeRate.Rate,
                exchangeRate.RateDate,
                record.CreatedAt);
        }

        var transfer = new Transfer(
            outbound,
            inbound,
            exchangeRate?.Rate,
            exchangeRate?.RateDate,
            record.CreatedAt);
        context.FinancialTransactions.AddRange(outbound, inbound);
        context.Transfers.Add(transfer);
        await context.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);

        return Result(TransferRecordOutcome.Succeeded, new TransferSnapshot(
            transfer.PublicId,
            outbound.PublicId,
            inbound.PublicId,
            origin.PublicId,
            destination.PublicId,
            outbound.Amount,
            origin.Currency.Code,
            inbound.Amount,
            destination.Currency.Code,
            transfer.AppliedRate,
            transfer.RateDate,
            record.OccurredOn,
            transfer.CreatedAt));
    }

    private static TransferRecordResult Result(
        TransferRecordOutcome outcome,
        TransferSnapshot? transfer = null) => new(transfer, outcome);
}
