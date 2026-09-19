using System.Text.RegularExpressions;

namespace ArturRios.Fortuna.Command.Auditing;

/// <summary>
///     Derives the audited entity type from a command name: the command suffix and the leading
///     verb are removed, so every command acting on one entity records the same type
///     (<c>CreateFinancialAccountCommand</c>, <c>HardDeleteFinancialAccountCommand</c> and
///     <c>RestoreFinancialAccountCommand</c> all record <c>FinancialAccount</c>).
/// </summary>
public static partial class AuditEntityTypes
{
    private static readonly string[] Verbs =
    [
        "HardDelete", "Create", "Update", "Delete", "Restore", "Record", "Define", "Reauthenticate",
        "Revoke", "Regenerate", "Reconcile", "Settle", "Close", "Synchronize", "Retry", "Request",
        "Grant", "Withdraw", "Erase", "Authenticate", "Recover", "Materialize", "Import"
    ];

    // Commands whose name does not end in the entity their output identifies.
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        ["AttachDocument"] = "Attachment",
        ["AttachTransactionTag"] = "Transaction",
        ["DetachTransactionTag"] = "Transaction",
        ["MergeCounterparties"] = "Counterparty",
        ["ReassignCategoryTransactions"] = "Category",
        ["RegenerateLocalAccountRecoveryCodes"] = "LocalAccount",
        ["MaterializeRecurringTransactions"] = "RecurringTransaction",
        ["SynchronizeExchangeRates"] = "ExchangeRate",
        ["RecordManualExchangeRate"] = "ExchangeRate",
        ["ImportExcelWorkbook"] = "ImportJob",
        ["ImportPdfInvoice"] = "ImportJob"
    };

    public static string Resolve(string commandName)
    {
        var name = ThroughApiSuffix().Replace(commandName, string.Empty);
        if (Overrides.TryGetValue(name, out var entityType))
        {
            return entityType;
        }

        foreach (var verb in Verbs)
        {
            if (name.Length > verb.Length &&
                name.StartsWith(verb, StringComparison.Ordinal) &&
                char.IsUpper(name[verb.Length]))
            {
                return name[verb.Length..];
            }
        }

        return name;
    }

    [GeneratedRegex("(ThroughApi)?Command$")]
    private static partial Regex ThroughApiSuffix();
}
