using ArturRios.Fortuna.Domain.Jobs;

namespace ArturRios.Fortuna.Shared.Jobs;

public interface IBackgroundJobStore
{
    Task<BackgroundJob?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<BackgroundJob>> RecoverAsync(CancellationToken cancellationToken);
    Task SaveAsync(BackgroundJob job, CancellationToken cancellationToken);
}
