using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Shared.Ingestion;

public interface IConnectionStore
{
    Task<ConnectionMutationResult> CreateAsync(
        ConnectionCreation creation,
        CancellationToken cancellationToken);

}

public interface IConnectionReauthenticationStore
{
    Task<ConnectionReauthenticationResult> ReauthenticateAsync(
        ConnectionReauthentication reauthentication,
        CancellationToken cancellationToken);
}

public interface IConnectionRevocationStore
{
    Task<ConnectionRevocationResult> RevokeAsync(
        ConnectionRevocation revocation,
        CancellationToken cancellationToken);
}

public interface IConnectionReader
{
    IQueryable<Connection> Query();

    Task<ConnectionSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);
}

public enum ConnectionMutationOutcome
{
    Succeeded = 1,
    Duplicate = 2
}

public sealed record ConnectionCreation(
    Guid UserId,
    TransactionSourceType DataSourceType,
    string ExternalReference,
    byte[] AccessTokenCipher,
    DateTimeOffset CreatedAt);

public sealed record ConnectionMutationResult(
    ConnectionSnapshot Connection,
    ConnectionMutationOutcome Outcome);

public sealed record ConnectionReauthentication(
    Guid UserId,
    Guid ConnectionId,
    string ExternalReference,
    byte[] AccessTokenCipher,
    DateTimeOffset UpdatedAt);

public enum ConnectionReauthenticationOutcome
{
    Succeeded = 1,
    NotFound = 2,
    NotRequired = 3,
    Revoked = 4,
    DuplicateReference = 5
}

public sealed record ConnectionReauthenticationResult(
    ConnectionSnapshot? Connection,
    ConnectionReauthenticationOutcome Outcome);

public sealed record ConnectionRevocation(
    Guid UserId,
    Guid ConnectionId,
    DateTimeOffset UpdatedAt);

public enum ConnectionRevocationOutcome
{
    Succeeded = 1,
    AlreadyRevoked = 2,
    NotFound = 3
}

public sealed record ConnectionRevocationResult(
    ConnectionSnapshot? Connection,
    int StoppedSynchronizations,
    ConnectionRevocationOutcome Outcome);

public sealed record ConnectionSnapshot(
    Guid Id,
    TransactionSourceType DataSourceType,
    string ExternalReference,
    ConnectionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
