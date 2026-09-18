using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Data.Transactions;

/// <summary>
/// The single definition of which transactions every read path treats as live and which of them
/// feed income and expense figures. Aggregations, drill-down, search totals and the table report
/// all go through these members, so a record excluded from a chart is excluded from the rows
/// behind it too. The LINQ and SQL forms state the same rule and must change together.
/// </summary>
internal static class TransactionVisibility
{
    /// <summary>
    /// A live transaction is not soft-deleted and neither is its category, account or card.
    /// </summary>
    public static IQueryable<FinancialTransaction> WhereLive(
        this IQueryable<FinancialTransaction> transactions) => transactions.Where(transaction =>
        !transaction.IsDeleted &&
        !transaction.Category.IsDeleted &&
        (transaction.FinancialAccount == null || !transaction.FinancialAccount.IsDeleted) &&
        (transaction.CreditCard == null || !transaction.CreditCard.IsDeleted));

    /// <summary>
    /// A transfer leg moves money between the owner's own pockets and is neither an earning nor
    /// an expense (BR-15), so it never feeds income and expense figures.
    /// </summary>
    public static IQueryable<FinancialTransaction> WhereNotTransfer(
        this IQueryable<FinancialTransaction> transactions,
        AppDbContext context) => transactions.Where(transaction =>
        !context.Transfers.Any(transfer =>
            transfer.OutboundTransactionId == transaction.Id ||
            transfer.InboundTransactionId == transaction.Id));

    /// <summary>
    /// The SQL form of <see cref="WhereLive"/> over a transaction joined to its category and,
    /// with left joins, to its account and card.
    /// </summary>
    public static string LiveSql(string transaction, string category, string account, string card) =>
        $"NOT {transaction}.is_deleted " +
        $"AND NOT {category}.is_deleted " +
        $"AND ({account}.id IS NULL OR NOT {account}.is_deleted) " +
        $"AND ({card}.id IS NULL OR NOT {card}.is_deleted)";

    /// <summary>
    /// The SQL form of <see cref="WhereNotTransfer"/>; <paramref name="transferTable"/> is the
    /// provider-qualified name of the transfer table.
    /// </summary>
    public static string NotTransferSql(string transaction, string transferTable) =>
        $"NOT EXISTS (SELECT 1 FROM {transferTable} transfer " +
        $"WHERE transfer.outbound_transaction_id = {transaction}.id " +
        $"OR transfer.inbound_transaction_id = {transaction}.id)";
}
