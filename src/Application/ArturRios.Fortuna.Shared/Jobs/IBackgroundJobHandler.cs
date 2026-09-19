using ArturRios.Output;

namespace ArturRios.Fortuna.Shared.Jobs;

public interface IBackgroundJobHandler
{
    string JobType { get; }

    /// <summary>
    /// Executes one job. Expected failures (invalid payload, work item gone, source unavailable)
    /// are returned as errors on the output; the processor fails the job with them.
    /// </summary>
    Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken);
}
