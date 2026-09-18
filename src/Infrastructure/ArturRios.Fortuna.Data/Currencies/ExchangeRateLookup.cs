using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Currencies;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Currencies;

// The rate that applies on a day is the latest one dated on or before it; on the same day the
// higher source (manual over published) wins.
internal static class ExchangeRateLookup
{
    public static IQueryable<ExchangeRate> Applicable(
        AppDbContext context,
        string baseCurrencyCode,
        string quoteCurrencyCode,
        DateOnly onOrBefore) => context.ExchangeRates
        .AsNoTracking()
        .Where(rate =>
            rate.BaseCurrency.Code == baseCurrencyCode &&
            rate.QuoteCurrency.Code == quoteCurrencyCode &&
            rate.RateDate <= onOrBefore)
        .OrderByDescending(rate => rate.RateDate)
        .ThenByDescending(rate => rate.Source);

    public static Task<ExchangeRate?> FindLatestAsync(
        AppDbContext context,
        string baseCurrencyCode,
        string quoteCurrencyCode,
        DateOnly onOrBefore,
        CancellationToken cancellationToken) => Applicable(
            context,
            baseCurrencyCode,
            quoteCurrencyCode,
            onOrBefore)
        .FirstOrDefaultAsync(cancellationToken);
}
