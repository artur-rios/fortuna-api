using ArturRios.Fortuna.Domain.Guards;

namespace ArturRios.Fortuna.Domain.Jobs;

public enum BackgroundJobState : short
{
    Pending = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4
}

public sealed class BackgroundJob
{
    private BackgroundJob()
    {
    }

    private BackgroundJob(string type, string payload, string idempotencyKey, string? correlationId, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        Type = BoundedText.Required(type, 100, nameof(type), "A job type is required.");
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey))
            : idempotencyKey;
        CorrelationId = correlationId;
        CreatedAt = createdAt;
        State = BackgroundJobState.Pending;
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? CorrelationId { get; private set; }
    public BackgroundJobState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }

    public static BackgroundJob Create(
        string type,
        string payload,
        string idempotencyKey,
        string? correlationId,
        DateTimeOffset createdAt) => new(type, payload, idempotencyKey, correlationId, createdAt);

    private JobPhase Phase => State switch
    {
        BackgroundJobState.Pending => JobPhase.Pending,
        BackgroundJobState.Running => JobPhase.Running,
        BackgroundJobState.Succeeded => JobPhase.Finished,
        _ => JobPhase.Failed
    };

    public void Start(DateTimeOffset now)
    {
        JobLifecycle.EnsureCanStart(Phase, "job");
        State = BackgroundJobState.Running;
        StartedAt = now;
        CompletedAt = null;
        FailureReason = null;
    }

    public void Succeed(DateTimeOffset now)
    {
        JobLifecycle.EnsureCanComplete(Phase, "job");
        State = BackgroundJobState.Succeeded;
        CompletedAt = now;
    }

    public void Fail(string? reason, DateTimeOffset now)
    {
        JobLifecycle.EnsureCanFail(Phase, "job");
        State = BackgroundJobState.Failed;
        FailureReason = JobLifecycle.FailureReason(reason);
        CompletedAt = now;
    }

    public void Requeue()
    {
        if (State is not (BackgroundJobState.Pending or BackgroundJobState.Running))
        {
            throw new InvalidOperationException($"A {State} job cannot be requeued.");
        }

        State = BackgroundJobState.Pending;
        StartedAt = null;
        CompletedAt = null;
        FailureReason = null;
    }

    public void Retry()
    {
        JobLifecycle.EnsureCanRetry(Phase, "job");
        State = BackgroundJobState.Pending;
        StartedAt = null;
        CompletedAt = null;
        FailureReason = null;
    }
}
