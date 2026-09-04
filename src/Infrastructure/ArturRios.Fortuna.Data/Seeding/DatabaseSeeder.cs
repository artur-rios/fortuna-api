using System.Reflection;
using System.Text.Json;
using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Currencies;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Seeding;

public sealed class DatabaseSeeder(AppDbContext context)
{
    private const long CurrencySeedLockId = 0x43555252454E4359;
    private static readonly HashSet<string> ZeroMinorUnits =
        ["BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XAU", "XBA", "XBB", "XBC", "XBD", "XDR", "XOF", "XPD", "XPF", "XPT", "XSU", "XTS", "XUA", "XXX"];
    private static readonly HashSet<string> ThreeMinorUnits = ["BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({CurrencySeedLockId})",
            cancellationToken);
        var existing = await context.Currencies.Select(x => x.Code).ToHashSetAsync(cancellationToken);
        var missing = LoadCurrencies().Where(x => !existing.Contains(x.Code));
        context.Currencies.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static IReadOnlyList<Currency> LoadCurrencies()
    {
        const string resourceName = "ArturRios.Fortuna.Data.Seeding.iso_4217.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded currency resource '{resourceName}' was not found.");
        var document = JsonSerializer.Deserialize<CurrencyDocument>(stream)
            ?? throw new InvalidOperationException("The ISO 4217 currency resource is invalid.");
        return document.Currencies.Select(item => new Currency(item.Code, item.Name, MinorUnits(item.Code))).ToArray();
    }

    private static short MinorUnits(string code) => code switch
    {
        "CLF" => 4,
        _ when ThreeMinorUnits.Contains(code) => 3,
        _ when ZeroMinorUnits.Contains(code) => 0,
        _ => 2
    };

    private sealed class CurrencyDocument
    {
        [System.Text.Json.Serialization.JsonPropertyName("4217")]
        public CurrencyItem[] Currencies { get; init; } = [];
    }

    private sealed class CurrencyItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("alpha_3")]
        public string Code { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }
}
