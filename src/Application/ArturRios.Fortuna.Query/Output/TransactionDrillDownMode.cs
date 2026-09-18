namespace ArturRios.Fortuna.Query.Output;

public enum TransactionDrillDownMode
{
    Transaction = 1,
    Transactions = 2,
    Aggregation = 3
}

public static class TransactionDrillDownModeExtensions
{
    public static string WireName(this TransactionDrillDownMode mode) =>
        mode.ToString().ToLowerInvariant();
}
