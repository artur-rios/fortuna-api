using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ArturRios.Fortuna.Shared.Jobs;

public static class JobPayload
{
    /// <summary>Reads a job payload; a malformed or empty payload is reported, not thrown.</summary>
    public static bool TryRead<T>(
        string payload,
        [NotNullWhen(true)] out T? value,
        JsonSerializerOptions? options = null)
        where T : class
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(payload, options);
        }
        catch (JsonException)
        {
            value = null;
        }

        return value is not null;
    }
}
