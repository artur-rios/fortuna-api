using System.Security.Cryptography;
using System.Text.Json;
using ArturRios.Fortuna.Shared.Reporting;
using Microsoft.AspNetCore.DataProtection;

namespace ArturRios.Fortuna.WebApi.Services;

public sealed class DataProtectionTransactionDrillDownKeyCodec : ITransactionDrillDownKeyCodec
{
    private readonly IDataProtector protector;

    public DataProtectionTransactionDrillDownKeyCodec(IDataProtectionProvider provider)
    {
        protector = provider.CreateProtector("Fortuna.Reporting.TransactionDrillDown.v1");
    }

    public string Encode(TransactionDrillDownKeyPayload payload) =>
        protector.Protect(JsonSerializer.Serialize(payload));

    public bool TryDecode(string key, out TransactionDrillDownKeyPayload? payload)
    {
        payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<TransactionDrillDownKeyPayload>(
                protector.Unprotect(key));
            return payload is not null;
        }
        catch (Exception exception) when (exception is CryptographicException or
            JsonException or FormatException)
        {
            return false;
        }
    }
}
