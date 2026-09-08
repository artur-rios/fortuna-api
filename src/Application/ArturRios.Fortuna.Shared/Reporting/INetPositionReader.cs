namespace ArturRios.Fortuna.Shared.Reporting;

public interface INetPositionReader
{
    Task<IReadOnlyCollection<NetPositionCurrencySnapshot>> ReadAsync(
        Guid userId,
        DateOnly asOf,
        CancellationToken cancellationToken);
}

public sealed record NetPositionCurrencySnapshot(
    string CurrencyCode,
    decimal FinancialAccounts,
    decimal Investments,
    decimal CreditCards)
{
    public decimal Net => FinancialAccounts + Investments - CreditCards;
}
