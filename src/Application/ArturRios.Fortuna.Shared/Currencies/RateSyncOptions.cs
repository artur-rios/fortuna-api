namespace ArturRios.Fortuna.Shared.Currencies;

public sealed record RateSyncOptions(
    Uri? SourceBaseUri,
    string? Cron,
    IReadOnlyCollection<string> Currencies)
{
    public bool IsConfigured => SourceBaseUri is not null;
}
