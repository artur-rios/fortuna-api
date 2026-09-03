namespace ArturRios.Fortuna.Shared.Jobs;

public interface IBackgroundJobQueue
{
    int Depth { get; }
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken);
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
