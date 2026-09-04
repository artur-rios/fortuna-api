namespace ArturRios.Fortuna.Shared.Currencies;

public static class ExchangeRateSyncJob
{
    public const string Type = "exchange-rate-sync";
}

public sealed record ExchangeRateSyncJobPayload(DateOnly RequestedDate);
