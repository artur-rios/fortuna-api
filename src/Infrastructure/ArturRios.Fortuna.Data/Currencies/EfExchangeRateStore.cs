using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Shared.Currencies;
using ArturRios.Fortuna.Shared.Messages;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Currencies;

public sealed class EfExchangeRateStore(AppDbContext context) : IExchangeRateStore, IExchangeRateReader
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

    public async Task<ManualRateUpsertResult> UpsertManualAsync(
        ManualRateCandidate rate,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({RateSyncLockId})",
            cancellationToken);

        var currencies = await context.Currencies
            .Where(currency =>
                currency.Code == rate.BaseCurrencyCode ||
                currency.Code == rate.QuoteCurrencyCode)
            .ToDictionaryAsync(currency => currency.Code, cancellationToken);
        if (!currencies.TryGetValue(rate.BaseCurrencyCode, out var baseCurrency) ||
            !currencies.TryGetValue(rate.QuoteCurrencyCode, out var quoteCurrency))
        {
            throw new InvalidOperationException(ManualExchangeRateMessages.CurrencyNotSupported);
        }

        var current = await context.ExchangeRates.SingleOrDefaultAsync(
            candidate =>
                candidate.BaseCurrencyId == baseCurrency.Id &&
                candidate.QuoteCurrencyId == quoteCurrency.Id &&
                candidate.RateDate == rate.RateDate &&
                candidate.Source == ExchangeRateSource.Manual,
            cancellationToken);
        var replacedExisting = current is not null;
        if (current is null)
        {
            current = new ExchangeRate(
                baseCurrency.Id,
                quoteCurrency.Id,
                rate.Rate,
                rate.RateDate,
                ExchangeRateSource.Manual);
            context.ExchangeRates.Add(current);
        }
        else
        {
            current.ReplaceManualRate(rate.Rate);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ManualRateUpsertResult(current.Rate, replacedExisting);
    }

    public async Task<ExchangeRateSnapshot?> FindApplicableAsync(
        string baseCurrencyCode,
        string quoteCurrencyCode,
        DateOnly figureDate,
        CancellationToken cancellationToken) =>
        await context.ExchangeRates
            .AsNoTracking()
            .Where(rate =>
                rate.BaseCurrency.Code == baseCurrencyCode &&
                rate.QuoteCurrency.Code == quoteCurrencyCode &&
                rate.RateDate <= figureDate)
            .OrderByDescending(rate => rate.RateDate)
            .ThenByDescending(rate => rate.Source)
            .Select(rate => new ExchangeRateSnapshot(
                rate.BaseCurrency.Code,
                rate.QuoteCurrency.Code,
                rate.Rate,
                rate.RateDate,
                rate.Source))
            .FirstOrDefaultAsync(cancellationToken);
}
