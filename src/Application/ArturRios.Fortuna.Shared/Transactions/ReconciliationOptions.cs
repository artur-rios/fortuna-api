namespace ArturRios.Fortuna.Shared.Transactions;

public sealed record ReconciliationOptions(
    decimal AmountTolerance,
    int DateToleranceDays);
