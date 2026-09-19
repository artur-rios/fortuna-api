namespace ArturRios.Fortuna.Domain.Jobs;

/// <summary>
/// The phases shared by every kind of asynchronous work: background jobs, import jobs and data
/// exports. Their status enums differ only in naming and each maps onto these phases.
/// </summary>
internal enum JobPhase
{
    Pending,
    Running,
    Finished,
    Failed
}

/// <summary>The one state machine behind BackgroundJob, ImportJob and DataExport.</summary>
internal static class JobLifecycle
{
    public const int FailureReasonMaximumLength = 1000;
    public const string DefaultFailureReason = "The job failed.";

    public static void EnsureCanStart(JobPhase phase, string subject) =>
        Ensure(phase == JobPhase.Pending, $"Only a pending {subject} can start.");

    public static void EnsureCanComplete(JobPhase phase, string subject) =>
        Ensure(phase == JobPhase.Running, $"Only a running {subject} can complete.");

    public static void EnsureCanFail(JobPhase phase, string subject) =>
        Ensure(phase is JobPhase.Pending or JobPhase.Running, $"Only an unfinished {subject} can fail.");

    public static void EnsureCanRetry(JobPhase phase, string subject) =>
        Ensure(phase == JobPhase.Failed, $"Only a failed {subject} can be retried.");

    /// <summary>
    /// Normalizes a failure reason. Failing must not itself fail, so a blank reason falls back to
    /// a generic one and an oversized reason (often an exception message) is truncated to fit.
    /// </summary>
    public static string FailureReason(string? reason)
    {
        var normalized = reason?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return DefaultFailureReason;
        }

        return normalized.Length <= FailureReasonMaximumLength
            ? normalized
            : normalized[..FailureReasonMaximumLength];
    }

    private static void Ensure(bool allowed, string message)
    {
        if (!allowed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
