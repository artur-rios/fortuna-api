using ArturRios.Fortuna.Domain.Users;

namespace ArturRios.Fortuna.Shared.Users;

public interface ILocalAccountStore
{
    Task<bool> ExistsAsync(CancellationToken cancellationToken);

    Task<LocalAccountCredentialSnapshot?> FindForAuthenticationAsync(
        string name,
        CancellationToken cancellationToken);

    Task<LocalAccountCreationResult> CreateAsync(
        LocalAccountCreation creation,
        CancellationToken cancellationToken);

    Task<LocalAccountRecoveryResult> RecoverAsync(
        LocalAccountRecovery recovery,
        CancellationToken cancellationToken);
}

public sealed record LocalAccountCreation(
    string DisplayName,
    byte[] SecretHash,
    byte[] Salt,
    LocalAccountStorageMode StorageMode,
    IReadOnlyCollection<byte[]> RecoveryCodeHashes,
    DateTimeOffset CreatedAt);

public sealed record LocalAccountSnapshot(
    Guid Id,
    Guid UserId,
    string DisplayName,
    LocalAccountStorageMode StorageMode,
    DateTimeOffset CreatedAt);

public sealed record LocalAccountCredentialSnapshot(
    Guid UserId,
    string DisplayName,
    byte[] SecretHash,
    byte[] Salt);

public sealed record LocalAccountCreationResult(
    LocalAccountSnapshot? Account,
    bool AlreadyExists);

public sealed record LocalAccountRecovery(
    string Name,
    byte[] RecoveryCodeHash,
    byte[] NewSecretHash,
    byte[] NewSalt,
    DateTimeOffset RecoveredAt);

public enum LocalAccountRecoveryStatus
{
    Recovered,
    InvalidCode,
    Exhausted
}

public sealed record LocalAccountRecoverySnapshot(
    Guid UserId,
    string DisplayName,
    int RemainingRecoveryCodes);

public sealed record LocalAccountRecoveryResult(
    LocalAccountRecoveryStatus Status,
    LocalAccountRecoverySnapshot? Account);
