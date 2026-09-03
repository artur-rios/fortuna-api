using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Users;

namespace ArturRios.Fortuna.WebApi.Services;

/// <summary>
/// The built-in host supports the portable in-memory mode. An operating-system credential-store
/// adapter must replace this service on a host that supplies one.
/// </summary>
public sealed class LocalCredentialStoreAvailability : ILocalCredentialStoreAvailability
{
    public bool IsAvailable(LocalAccountStorageMode storageMode) =>
        storageMode == LocalAccountStorageMode.InMemory;
}
