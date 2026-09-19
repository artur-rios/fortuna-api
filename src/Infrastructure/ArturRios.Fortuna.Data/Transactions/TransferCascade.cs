using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Transactions;

// Carries a parent's soft deletion (or restore) across the transfers its transactions take part
// in, so the transfer and both of its legs share one lifecycle instead of leaving the other
// leg behind as a plain income or expense. Transfers that settled a credit card statement are
// left alone: the settlement is frozen and is handled by the statement lifecycle.
internal static class TransferCascade
{
    public static async Task<IReadOnlyCollection<FinancialTransaction>> SoftDeleteAsync(
        AppDbContext context,
        IReadOnlyCollection<FinancialTransaction> transactions,
        Guid cascadeId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var linkedLegs = new List<FinancialTransaction>();
        foreach (var transfer in await LinkedTransfersAsync(context, transactions, cancellationToken))
        {
            if (transfer.IsDeleted)
            {
                continue;
            }

            transfer.SoftDeleteFromCascade(cascadeId, changedAt);
            foreach (var leg in Legs(transfer))
            {
                if (leg.SoftDeleteFromCascade(cascadeId, changedAt))
                {
                    linkedLegs.Add(leg);
                }
            }

            transfer.InboundInvestmentMovement?.SoftDeleteFromCascade(cascadeId, changedAt);
        }

        return linkedLegs;
    }

    public static async Task<IReadOnlyCollection<FinancialTransaction>> RestoreAsync(
        AppDbContext context,
        IReadOnlyCollection<FinancialTransaction> transactions,
        Guid cascadeId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var linkedLegs = new List<FinancialTransaction>();
        foreach (var transfer in await LinkedTransfersAsync(context, transactions, cancellationToken))
        {
            // A leg whose own account was deleted meanwhile stays deleted with the transfer.
            if (transfer.DeletionCascadeId != cascadeId ||
                Legs(transfer).Any(leg => leg.FinancialAccount?.IsDeleted == true &&
                    !transactions.Contains(leg)))
            {
                continue;
            }

            transfer.RestoreFromCascade(cascadeId, changedAt);

            foreach (var leg in Legs(transfer))
            {
                if (leg.RestoreFromCascade(cascadeId, changedAt))
                {
                    linkedLegs.Add(leg);
                }
            }

            transfer.InboundInvestmentMovement?.RestoreFromCascade(cascadeId, changedAt);
        }

        return linkedLegs;
    }

    private static async Task<IReadOnlyCollection<Transfer>> LinkedTransfersAsync(
        AppDbContext context,
        IReadOnlyCollection<FinancialTransaction> transactions,
        CancellationToken cancellationToken)
    {
        var ids = transactions.Select(transaction => transaction.Id).ToArray();

        return await context.Transfers
            .Include(transfer => transfer.OutboundTransaction)
                .ThenInclude(transaction => transaction.FinancialAccount)
            .Include(transfer => transfer.InboundTransaction)
                .ThenInclude(transaction => transaction!.FinancialAccount)
            .Include(transfer => transfer.InboundInvestmentMovement)
            .Where(transfer =>
                (ids.Contains(transfer.OutboundTransactionId) ||
                    (transfer.InboundTransactionId.HasValue &&
                        ids.Contains(transfer.InboundTransactionId.Value))) &&
                !context.CreditCardStatements.Any(statement =>
                    transfer.InboundTransactionId.HasValue &&
                    statement.SettlementTransactionId == transfer.InboundTransactionId))
            .ToListAsync(cancellationToken);
    }

    private static IEnumerable<FinancialTransaction> Legs(Transfer transfer)
    {
        yield return transfer.OutboundTransaction;

        if (transfer.InboundTransaction is not null)
        {
            yield return transfer.InboundTransaction;
        }
    }
}
