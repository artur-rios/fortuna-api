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
    Task<IReadOnlyList<BackgroundJob>> RecoverAsync(CancellationToken cancellationToken);
    Task SaveAsync(BackgroundJob job, CancellationToken cancellationToken);
}
