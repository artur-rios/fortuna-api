namespace ArturRios.Fortuna.Domain.Currencies;

/// <summary>Minor-unit rules shared by every monetary amount held in a currency.</summary>
public static class Money
{
    /// <summary>The smallest representable step of the currency, e.g. 0.01 for BRL, 1 for JPY.</summary>
    public static decimal MinorUnit(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        var unit = 1m;
        for (var digit = 0; digit < currency.MinorUnitDigits; digit++)
        {
            unit /= 10m;
        }

        return unit;
    }

    /// <summary>Whether the amount carries no more decimal places than the currency allows.</summary>
    public static bool FitsScale(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        return decimal.Round(amount, currency.MinorUnitDigits) == amount;
    }

    /// <summary>Rounds the amount to the currency's minor unit, half away from zero.</summary>
    public static decimal Round(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        return decimal.Round(amount, currency.MinorUnitDigits, MidpointRounding.AwayFromZero);
    }
}
