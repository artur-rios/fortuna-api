using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArturRios.Fortuna.Data.Ingestion;

public sealed class EfConnectionStore(AppDbContext context)
    : IConnectionStore, IConnectionReader, IConnectionReauthenticationStore,
        IConnectionRevocationStore
{
    public async Task<ConnectionMutationResult> CreateAsync(
        ConnectionCreation creation,
        CancellationToken cancellationToken)
    {
        var existing = await FindByExternalReferenceAsync(
            creation.UserId,
            creation.DataSourceType,
            creation.ExternalReference,
            cancellationToken);
        if (existing is not null)
        {
            return new ConnectionMutationResult(existing, ConnectionMutationOutcome.Duplicate);
        }

        var user = await context.UserProfiles.SingleAsync(
            item => item.PublicId == creation.UserId,
            cancellationToken);
        var connection = new Connection(
            user,
            creation.DataSourceType,
            creation.ExternalReference,
            creation.AccessTokenCipher,
            creation.CreatedAt);
        context.Connections.Add(connection);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ix_connection_user_id_data_source_type_external_reference"
            })
        {
            context.Entry(connection).State = EntityState.Detached;
            var duplicate = await FindByExternalReferenceAsync(
                creation.UserId,
                creation.DataSourceType,
                creation.ExternalReference,
                cancellationToken);
            return new ConnectionMutationResult(
                duplicate ?? throw new InvalidOperationException(
                    "The duplicate connection could not be resolved."),
                ConnectionMutationOutcome.Duplicate);
        }

        return new ConnectionMutationResult(
            Snapshot(connection),
            ConnectionMutationOutcome.Succeeded);
    }

    public IQueryable<Connection> Query() => context.Connections.AsNoTracking();

    public async Task<ConnectionSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var connection = await Connections().SingleOrDefaultAsync(item =>
            item.User.PublicId == userId && item.PublicId == id,
            cancellationToken);
        return connection is null ? null : Snapshot(connection);
    }

    public async Task<ConnectionReauthenticationResult> ReauthenticateAsync(
        ConnectionReauthentication reauthentication,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var connection = await ConnectionRowLock.FindOwnedAsync(
            context,
            reauthentication.UserId,
            reauthentication.ConnectionId,
            cancellationToken);
        if (connection is null)
        {
            return ReauthenticationResult(ConnectionReauthenticationOutcome.NotFound);
        }

        if (connection.Status == ConnectionStatus.Revoked)
        {
            return ReauthenticationResult(ConnectionReauthenticationOutcome.Revoked, connection);
        }

        if (connection.Status != ConnectionStatus.RequiresReauthentication)
        {
            return ReauthenticationResult(ConnectionReauthenticationOutcome.NotRequired, connection);
        }

        connection.Reauthenticate(
            reauthentication.ExternalReference,
            reauthentication.AccessTokenCipher,
            reauthentication.UpdatedAt);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ix_connection_user_id_data_source_type_external_reference"
            })
        {
            await transaction.RollbackAsync(cancellationToken);
            context.Entry(connection).State = EntityState.Detached;
            return ReauthenticationResult(ConnectionReauthenticationOutcome.DuplicateReference);
        }

        return ReauthenticationResult(ConnectionReauthenticationOutcome.Succeeded, connection);
    }

    public async Task<ConnectionRevocationResult> RevokeAsync(
        ConnectionRevocation revocation,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var connection = await ConnectionRowLock.FindOwnedAsync(
            context,
            revocation.UserId,
            revocation.ConnectionId,
            cancellationToken);
        if (connection is null)
        {
            return new ConnectionRevocationResult(
                null, 0, ConnectionRevocationOutcome.NotFound);
        }

        var changed = connection.Revoke(revocation.UpdatedAt);
        var stopped = 0;
        if (changed)
        {
            var jobs = await context.ImportJobs.Where(job =>
                job.ConnectionId == connection.Id &&
                (job.Status == ImportJobStatus.Pending ||
                    job.Status == ImportJobStatus.Running))
                .ToArrayAsync(cancellationToken);
            foreach (var job in jobs)
            {
                job.Fail(ConnectionMessages.SynchronizationStoppedByRevocation,
                    revocation.UpdatedAt);
            }

            stopped = jobs.Length;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new ConnectionRevocationResult(
            Snapshot(connection),
            stopped,
            changed
                ? ConnectionRevocationOutcome.Succeeded
                : ConnectionRevocationOutcome.AlreadyRevoked);
    }

    private IQueryable<Connection> Connections() => context.Connections.Include(item => item.User);

    private static ConnectionSnapshot Snapshot(Connection connection) => new(
        connection.PublicId,
        connection.DataSourceType,
        connection.ExternalReference,
        connection.Status,
        connection.CreatedAt,
        connection.UpdatedAt);

    private async Task<ConnectionSnapshot?> FindByExternalReferenceAsync(
        Guid userId,
        TransactionSourceType dataSourceType,
        string externalReference,
        CancellationToken cancellationToken)
    {
        var connection = await Connections().SingleOrDefaultAsync(item =>
            item.User.PublicId == userId &&
            item.DataSourceType == dataSourceType &&
            item.ExternalReference == externalReference,
            cancellationToken);
        return connection is null ? null : Snapshot(connection);
    }

    private static ConnectionReauthenticationResult ReauthenticationResult(
        ConnectionReauthenticationOutcome outcome,
        Connection? connection = null) => new(
        connection is null ? null : Snapshot(connection),
        outcome);
}
