using ArturRios.Fortuna.Shared.Users;
using Microsoft.Extensions.Caching.Memory;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
///     Process-local <see cref="IProvisionedProfileCache" />. An erased subject is kept as a
///     "not provisioned" entry for the same lifetime, so a request that provisioned it just before
///     the erasure cannot mark it provisioned again.
/// </summary>
public sealed class MemoryProvisionedProfileCache(IMemoryCache cache) : IProvisionedProfileCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly Lock gate = new();

    public bool IsProvisioned(Guid externalSubject) =>
        cache.TryGetValue(Key(externalSubject), out bool provisioned) && provisioned;

    public void MarkProvisioned(Guid externalSubject)
    {
        lock (gate)
        {
            if (cache.TryGetValue(Key(externalSubject), out bool provisioned) && !provisioned)
            {
                return;
            }

            cache.Set(Key(externalSubject), true, Lifetime);
        }
    }

    public void Forget(Guid externalSubject)
    {
        lock (gate)
        {
            cache.Set(Key(externalSubject), false, Lifetime);
        }
    }

    private static string Key(Guid externalSubject) => $"fortuna:provisioned-profile:{externalSubject:N}";
}
