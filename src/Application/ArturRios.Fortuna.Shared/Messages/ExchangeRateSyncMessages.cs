namespace ArturRios.Fortuna.Shared.Messages;

public static class ExchangeRateSyncMessages
{
    public const string Accepted = "Exchange-rate synchronization accepted.";
    public const string AlreadyQueued =
        "An exchange-rate synchronization for the requested date is already queued or running.";
    public const string AdministratorRequired =
        "Only a system administrator or the local installation owner can synchronize exchange rates.";
    public const string SourceNotConfigured = "The exchange-rate source is not configured.";
    public const string SourceUnavailable = "The exchange-rate source is unavailable.";
    public const string PublicationUnavailable = "No exchange-rate publication is available for the requested date.";
    public const string ConfiguredCurrencyNotFound = "A configured exchange-rate currency is not supported.";

    public static string CurrenciesMissing(IEnumerable<string> currencyCodes) =>
        $"The exchange-rate source published no usable rate for: {string.Join(", ", currencyCodes)}.";
}
