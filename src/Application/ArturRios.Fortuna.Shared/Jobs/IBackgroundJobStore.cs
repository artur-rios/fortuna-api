using ArturRios.Fortuna.Domain.Jobs;

namespace ArturRios.Fortuna.Shared.Jobs;

public interface IBackgroundJobStore
{
    Task<BackgroundJob> CreateAsync(
        string type,
        string payload,
        string idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken);

    Task<BackgroundJob?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<BackgroundJob?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);
    /// <summary>
    /// Finds a pending or running job of <paramref name="type"/> whose idempotency key starts
    /// with <paramref name="idempotencyKeyPrefix"/>, so repeated requests can reuse it.
    /// </summary>
    Task<BackgroundJob?> FindActiveAsync(
        string type,
        string idempotencyKeyPrefix,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BackgroundJob>> RecoverAsync(CancellationToken cancellationToken);
    Task SaveAsync(BackgroundJob job, CancellationToken cancellationToken);
}
