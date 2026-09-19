using ArturRios.Fortuna.Shared.Messages;

namespace ArturRios.Fortuna.WebApi.Controllers;

/// <summary>
///     The canonical message-to-status entries every controller shares. A controller merges its
///     own entries over these with <see cref="With" />; its own entry wins on a clash.
/// </summary>
public static class FortunaStatusMap
{
    public static readonly IReadOnlyDictionary<string, int> Shared = new Dictionary<string, int>
    {
        [UserProfileMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
        [AttachmentMessages.StorageUnavailable] = StatusCodes.Status503ServiceUnavailable,
        [DataExportMessages.StorageUnavailable] = StatusCodes.Status503ServiceUnavailable,
        [PersonalDataExportMessages.StorageUnavailable] = StatusCodes.Status503ServiceUnavailable,
        [TransactionMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
        [TransferMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
        [InstallmentPlanMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
        [InvestmentMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
        [CreditCardStatementMessages.ExchangeRateUnavailable] = StatusCodes.Status409Conflict,
        [ProcessingConsentMessages.ExternalDataProcessingRequired] = StatusCodes.Status403Forbidden,
        [ExchangeRateSyncMessages.AdministratorRequired] = StatusCodes.Status403Forbidden,
        [ManualExchangeRateMessages.AdministratorRequired] = StatusCodes.Status403Forbidden
    };

    /// <summary>Returns the shared entries with <paramref name="entries" /> merged over them.</summary>
    public static IReadOnlyDictionary<string, int> With(IReadOnlyDictionary<string, int> entries)
    {
        var merged = new Dictionary<string, int>(Shared);
        foreach (var (message, status) in entries)
        {
            merged[message] = status;
        }

        return merged;
    }
}
