using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.Seeding;
using ArturRios.Fortuna.Shared.Currencies;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Currencies;

public sealed class EfCurrencyReader(
    AppDbContext context,
    DatabaseSeeder seeder) : ICurrencyReader
{
    public async Task<IReadOnlyCollection<CurrencySnapshot>> ListAsync(
        CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(cancellationToken);

        return await context.Currencies
            .AsNoTracking()
            .OrderBy(currency => currency.Code)
            .Select(currency => new CurrencySnapshot(
                currency.Code,
                currency.Name,
                currency.MinorUnitDigits))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<CurrencySnapshot?> FindByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(cancellationToken);

        return await context.Currencies
            .AsNoTracking()
            .Where(currency => currency.Code == code)
            .Select(currency => new CurrencySnapshot(
                currency.Code,
                currency.Name,
                currency.MinorUnitDigits))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        if (!await context.Currencies.AnyAsync(cancellationToken))
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }
}
