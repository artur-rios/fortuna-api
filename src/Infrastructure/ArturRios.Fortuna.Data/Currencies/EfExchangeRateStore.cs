using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Currencies;

public sealed class EfExchangeRateStore(AppDbContext context) : IExchangeRateStore
{
    private const long RateSyncLockId = 0x5241544553594E43;

    public async Task<PublishedRateUpsertResult> UpsertPublishedAsync(
        IReadOnlyCollection<PublishedRateCandidate> rates,
        CancellationToken cancellationToken)
    {
        if (rates.Count == 0)
        {
            return new PublishedRateUpsertResult(0, 0);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({RateSyncLockId})",
            cancellationToken);

        var codes = rates
            .SelectMany(rate => new[] { rate.BaseCurrencyCode, rate.QuoteCurrencyCode })
            .ToHashSet(StringComparer.Ordinal);
        var currencies = await context.Currencies
            .Where(currency => codes.Contains(currency.Code))
            .ToDictionaryAsync(currency => currency.Code, cancellationToken);
        if (currencies.Count != codes.Count)
        {
            throw new InvalidOperationException(ExchangeRateSyncMessages.ConfiguredCurrencyNotFound);
        }

        var publicationDates = rates.Select(rate => rate.PublicationDate).ToHashSet();
        var existing = await context.ExchangeRates
            .Where(rate =>
                rate.Source == ExchangeRateSource.Published &&
                publicationDates.Contains(rate.RateDate))
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(rate =>
            (rate.BaseCurrencyId, rate.QuoteCurrencyId, rate.RateDate));
        var stored = 0;
        var unchanged = 0;

        foreach (var candidate in rates)
        {
            var baseId = currencies[candidate.BaseCurrencyCode].Id;
            var quoteId = currencies[candidate.QuoteCurrencyCode].Id;
            if (byKey.TryGetValue((baseId, quoteId, candidate.PublicationDate), out var current))
            {
                if (current.ReplacePublishedRate(candidate.Rate))
                {
                    stored++;
                }
                else
                {
                    unchanged++;
                }

                continue;
            }

            context.ExchangeRates.Add(new ExchangeRate(
                baseId,
                quoteId,
                candidate.Rate,
                candidate.PublicationDate,
                ExchangeRateSource.Published));
            stored++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PublishedRateUpsertResult(stored, unchanged);
    }
}
