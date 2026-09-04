namespace ArturRios.Fortuna.Shared.Currencies;

public interface ICurrencyReader
{
    Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(CancellationToken cancellationToken);

    Task<CurrencySnapshot?> FindByCodeAsync(
        string code,
        CancellationToken cancellationToken);
}

public sealed record CurrencySnapshot(
    string Code,
    string Name,
    short MinorUnitDigits);
