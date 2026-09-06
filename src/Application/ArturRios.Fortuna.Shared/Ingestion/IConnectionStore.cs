using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;

namespace ArturRios.Fortuna.Shared.Ingestion;

public interface IConnectionStore
{
    Task<ConnectionMutationResult> CreateAsync(
        ConnectionCreation creation,
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

public sealed record ConnectionSnapshot(
    Guid Id,
    TransactionSourceType DataSourceType,
    string ExternalReference,
    ConnectionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
