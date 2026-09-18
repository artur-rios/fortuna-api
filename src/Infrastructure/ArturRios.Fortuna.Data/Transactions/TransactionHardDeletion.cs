using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Investments;
using ArturRios.Fortuna.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Transactions;

// Expands a set of transactions that is about to be hard-deleted with everything that must
// disappear with it: both legs of any transfer (the transfer foreign keys cascade, so deleting
// one leg would silently leave the other behind as a plain income or expense), the investment
// movement a transfer paid into, and the installment plans the transactions belong to. Links
// that still point at live records outside the set are reported instead of being deleted.
internal sealed class TransactionHardDeletion
{
    public const string TransfersReference = "transfers";
    public const string InstallmentPlansReference = "installment plans";
    public const string StatementSettlementsReference = "statement settlements";

    private TransactionHardDeletion(
        IReadOnlyCollection<FinancialTransaction> transactions,
        IReadOnlyCollection<Transfer> transfers,
        IReadOnlyCollection<InvestmentMovement> investmentMovements,
        IReadOnlyCollection<InstallmentPlan> installmentPlans,
        IReadOnlyCollection<string> liveReferences)
    {
        Transactions = transactions;
        Transfers = transfers;
        InvestmentMovements = investmentMovements;
        InstallmentPlans = installmentPlans;
        LiveReferences = liveReferences;
    }

    public IReadOnlyCollection<FinancialTransaction> Transactions { get; }
    public IReadOnlyCollection<Transfer> Transfers { get; }
    public IReadOnlyCollection<InvestmentMovement> InvestmentMovements { get; }
    public IReadOnlyCollection<InstallmentPlan> InstallmentPlans { get; }
    public IReadOnlyCollection<string> LiveReferences { get; }

    public IReadOnlyCollection<long> TransactionIds =>
        Transactions.Select(transaction => transaction.Id).ToArray();

    public static async Task<TransactionHardDeletion> PlanAsync(
        AppDbContext context,
        IReadOnlyCollection<FinancialTransaction> transactions,
        IReadOnlyCollection<long> deletedStatementIds,
        CancellationToken cancellationToken)
    {
        var liveReferences = new SortedSet<string>(StringComparer.Ordinal);
        var all = transactions.ToDictionary(transaction => transaction.Id);
        var ids = all.Keys.ToArray();

        var transfers = await context.Transfers
            .Include(transfer => transfer.OutboundTransaction)
            .Include(transfer => transfer.InboundTransaction)
            .Include(transfer => transfer.InboundInvestmentMovement)
            .Where(transfer =>
                ids.Contains(transfer.OutboundTransactionId) ||
                (transfer.InboundTransactionId.HasValue &&
                    ids.Contains(transfer.InboundTransactionId.Value)))
            .ToListAsync(cancellationToken);
        var movements = new List<InvestmentMovement>();
        foreach (var transfer in transfers)
        {
            foreach (var leg in new[] { transfer.OutboundTransaction, transfer.InboundTransaction })
            {
                if (leg is null || all.ContainsKey(leg.Id))
                {
                    continue;
                }

                if (!leg.IsDeleted)
                {
                    liveReferences.Add(TransfersReference);
                }

                all.Add(leg.Id, leg);
            }

            if (transfer.InboundInvestmentMovement is { } movement)
            {
                if (!movement.IsDeleted)
                {
                    liveReferences.Add(TransfersReference);
                }

                movements.Add(movement);
            }
        }

        var planTransactionIds = all.Keys.ToArray();
        var plans = await context.InstallmentPlans
            .Include(plan => plan.Installments)
            .Where(plan => plan.Installments.Any(installment =>
                planTransactionIds.Contains(installment.Id)))
            .ToListAsync(cancellationToken);
        foreach (var installment in plans.SelectMany(plan => plan.Installments))
        {
            if (all.ContainsKey(installment.Id))
            {
                continue;
            }

            if (!installment.IsDeleted)
            {
                liveReferences.Add(InstallmentPlansReference);
            }

            all.Add(installment.Id, installment);
        }

        var allIds = all.Keys.ToArray();
        if (await context.CreditCardStatements.AnyAsync(statement =>
                statement.SettlementTransactionId.HasValue &&
                allIds.Contains(statement.SettlementTransactionId.Value) &&
                !deletedStatementIds.Contains(statement.Id),
                cancellationToken))
        {
            liveReferences.Add(StatementSettlementsReference);
        }

        return new TransactionHardDeletion(
            all.Values.ToArray(),
            transfers,
            movements,
            plans,
            liveReferences.ToArray());
    }

    public void Remove(AppDbContext context)
    {
        context.Transfers.RemoveRange(Transfers);
        context.InvestmentMovements.RemoveRange(InvestmentMovements);
        context.InstallmentPlans.RemoveRange(InstallmentPlans);
        context.FinancialTransactions.RemoveRange(Transactions);
    }
}
