using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Currencies;

namespace ArturRios.Fortuna.Query.Handlers;

internal static class CurrencyProjection
{
    public static CurrencyOutput From(CurrencySnapshot currency) => new()
    {
        Code = currency.Code,
        Name = currency.Name,
        MinorUnitDigits = currency.MinorUnitDigits
    };
}
