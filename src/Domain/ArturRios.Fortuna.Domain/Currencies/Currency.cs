namespace ArturRios.Fortuna.Domain.Currencies;

public sealed class Currency
{
    private Currency()
    {
    }

    public Currency(string code, string name, short minorUnitDigits)
    {
        if (code.Length != 3)
        {
            throw new ArgumentException("A currency code must contain three characters.", nameof(code));
        }

        if (minorUnitDigits is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(minorUnitDigits));
        }

        Code = code.ToUpperInvariant();
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A currency name is required.", nameof(name)) : name;
        MinorUnitDigits = minorUnitDigits;
    }

    public long Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public short MinorUnitDigits { get; private set; }
}
