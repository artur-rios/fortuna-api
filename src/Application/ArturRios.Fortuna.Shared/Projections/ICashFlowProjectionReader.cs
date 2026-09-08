namespace ArturRios.Fortuna.Shared.Projections;

public interface ICashFlowProjectionReader
{
    Task<CashFlowProjectionSnapshot> ReadAsync(
        Guid userId,
        DateOnly asOf,
        DateOnly through,
        DateOnly historyFrom,
        CancellationToken cancellationToken);
}

public enum CashFlowSourceKind
{
    Recurring = 1,
    Installment = 2,
    Statement = 3,
    Historical = 4
}

public sealed record CashFlowCurrencyAmountSnapshot(string CurrencyCode, decimal Amount);

public sealed record CashFlowAmountSnapshot(
    DateOnly Date,
    string CurrencyCode,
    decimal SignedAmount,
    CashFlowSourceKind Source);

public sealed record CashFlowProjectionSnapshot(
    IReadOnlyCollection<CashFlowCurrencyAmountSnapshot> StartingBalances,
    IReadOnlyCollection<CashFlowAmountSnapshot> FutureFigures,
    IReadOnlyCollection<CashFlowAmountSnapshot> HistoricalFigures,
    DateOnly? HistoryStartsOn);

public sealed record CashFlowProjectionOptions(
    int MaximumHorizonDays,
    int HistoricalLookbackDays,
    int MinimumHistoryDays);
