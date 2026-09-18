namespace ArturRios.Fortuna.Shared.Jobs;

/// <summary>Result of moving a job-backed work item (import job, export) to a new state.</summary>
public enum JobTransitionOutcome
{
    Applied = 1,

    /// <summary>The work item no longer exists, for example after its owner was erased.</summary>
    NotFound = 2,

    /// <summary>The work item already finished (or never started), so nothing changed.</summary>
    NotRunning = 3
}
