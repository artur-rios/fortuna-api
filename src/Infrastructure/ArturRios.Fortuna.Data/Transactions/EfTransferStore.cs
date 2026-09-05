using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Transactions;

public sealed class EfTransferStore(
    AppDbContext context,
    ITransactionLifecycleStore transactionLifecycle)
    : ITransferStore, ITransferReader, ITransferLifecycleStore
{
    public Task<TransferReadSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var transfers = context.Transfers
            .AsNoTracking()
            .Where(transfer => transfer.OutboundTransaction.User.PublicId == userId);
        if (!includeDeleted)
        {
            transfers = transfers.Where(transfer => !transfer.IsDeleted);
        }

        return transfers
            .Where(transfer => transfer.PublicId == id)
            .Select(transfer => new TransferReadSnapshot
            {
                Id = transfer.PublicId,
                OutboundTransactionId = transfer.OutboundTransaction.PublicId,
                InboundTransactionId = transfer.InboundTransaction == null
                    ? null
                    : transfer.InboundTransaction.PublicId,
                InboundInvestmentMovementId = transfer.InboundInvestmentMovement == null
                    ? null
                    : transfer.InboundInvestmentMovement.PublicId,
                OriginFinancialAccountId = transfer.OutboundTransaction.FinancialAccount!.PublicId,
                DestinationFinancialAccountId = transfer.InboundTransaction == null ||
                    transfer.InboundTransaction.FinancialAccount == null
                    ? null
                    : transfer.InboundTransaction.FinancialAccount.PublicId,
                DestinationCreditCardId = transfer.InboundTransaction == null ||
                    transfer.InboundTransaction.CreditCard == null
                    ? null
                    : transfer.InboundTransaction.CreditCard.PublicId,
                DestinationStatementId = transfer.InboundTransactionId == null
                    ? null
                    : context.CreditCardStatements
                        .Where(statement =>
                            statement.SettlementTransactionId == transfer.InboundTransactionId)
                        .Select(statement => (Guid?)statement.PublicId)
                        .SingleOrDefault(),
                DestinationInvestmentId = transfer.InboundInvestmentMovement == null
                    ? null
                    : transfer.InboundInvestmentMovement.Investment.PublicId,
                OutboundAmount = transfer.OutboundTransaction.Amount,
                OutboundCurrencyCode = transfer.OutboundTransaction.Currency.Code,
                InboundAmount = transfer.InboundTransaction == null
                    ? transfer.InboundInvestmentMovement!.Amount
                    : transfer.InboundTransaction.Amount,
                InboundCurrencyCode = transfer.InboundTransaction == null
                    ? transfer.InboundInvestmentMovement!.Investment.Currency.Code
                    : transfer.InboundTransaction.Currency.Code,
                AppliedRate = transfer.AppliedRate,
                RateDate = transfer.RateDate,
                OccurredOn = transfer.OutboundTransaction.OccurredOn,
                OutboundIsDeleted = transfer.OutboundTransaction.IsDeleted,
                InboundIsDeleted = transfer.InboundTransaction == null
                    ? transfer.InboundInvestmentMovement!.IsDeleted
                    : transfer.InboundTransaction.IsDeleted,
                IsDeleted = transfer.IsDeleted,
                CreatedAt = transfer.CreatedAt,
                UpdatedAt = transfer.UpdatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

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

    public Task<TransferLifecycleResult> SoftDeleteAsync(
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

    public Task<TransferLifecycleResult> RestoreAsync(
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

    private async Task<TransferLifecycleResult> ChangeLifecycleAsync(
        Guid userId,
        Guid id,
        Func<Guid, CancellationToken, Task<TransactionLifecycleResult>> change,
        CancellationToken cancellationToken)
    {
        var transfer = await context.Transfers
            .AsNoTracking()
            .Where(item =>
                item.PublicId == id &&
                item.OutboundTransaction.User.PublicId == userId)
            .Select(item => new
            {
                item.PublicId,
                OutboundTransactionId = item.OutboundTransaction.PublicId
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (transfer is null)
        {
            return LifecycleResult(TransferLifecycleOutcome.NotFound);
        }

        var result = await change(transfer.OutboundTransactionId, cancellationToken);
        return result.Outcome switch
        {
            TransactionLifecycleOutcome.Succeeded => LifecycleResult(
                TransferLifecycleOutcome.Succeeded,
                transfer.PublicId),
            TransactionLifecycleOutcome.NotFound => LifecycleResult(
                TransferLifecycleOutcome.NotFound),
            TransactionLifecycleOutcome.RestoreRequiresSoftDeletion => LifecycleResult(
                TransferLifecycleOutcome.RestoreRequiresSoftDeletion),
            TransactionLifecycleOutcome.SettledStatementFrozen => LifecycleResult(
                TransferLifecycleOutcome.SettledStatementFrozen),
            _ => throw new InvalidOperationException(
                "The delegated transaction lifecycle returned an unsupported outcome.")
        };
    }

    private static TransferRecordResult Result(
        TransferRecordOutcome outcome,
        TransferSnapshot? transfer = null) => new(transfer, outcome);

    private static TransferLifecycleResult LifecycleResult(
        TransferLifecycleOutcome outcome,
        Guid? id = null) => new(id, outcome);
}
