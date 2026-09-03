namespace ArturRios.Fortuna.Shared.Jobs;

public interface IBackgroundJobHandler
{
    string JobType { get; }
    Task ExecuteAsync(string payload, CancellationToken cancellationToken);
}
