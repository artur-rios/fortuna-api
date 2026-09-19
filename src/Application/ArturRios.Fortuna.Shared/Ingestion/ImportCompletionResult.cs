namespace ArturRios.Fortuna.Shared.Ingestion;

public enum ImportCompletionOutcome
{
    Completed = 1,

    /// <summary>The import job no longer exists.</summary>
    JobNotFound = 2,

    /// <summary>The import job is not running any more, so the batch was not applied.</summary>
    JobNotRunning = 3,

    /// <summary>The target account or card was deleted or disappeared while the job ran.</summary>
    TargetUnavailable = 4,

    /// <summary>The batch cannot be applied; <see cref="ImportCompletionResult.Reason"/> says why.</summary>
    Rejected = 5,

    /// <summary>The store stopped the job itself (for example on revocation) and recorded the reason.</summary>
    Stopped = 6
}

public sealed record ImportCompletionResult(ImportCompletionOutcome Outcome, string? Reason = null)
{
    public static ImportCompletionResult Completed { get; } = new(ImportCompletionOutcome.Completed);

    public static ImportCompletionResult Of(ImportCompletionOutcome outcome) => new(outcome);

    public static ImportCompletionResult Rejected(string reason) =>
        new(ImportCompletionOutcome.Rejected, reason);

    public static ImportCompletionResult Stopped(string reason) =>
        new(ImportCompletionOutcome.Stopped, reason);
}
