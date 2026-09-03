using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Shared.Users;

public interface ILocalCredentialStoreAvailability
{
    bool IsAvailable(LocalAccountStorageMode storageMode);
}
