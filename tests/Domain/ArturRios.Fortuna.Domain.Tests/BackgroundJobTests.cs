using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class BackgroundJobTests
{
    [UnitFact]
    public void GivenValidInput_WhenCreatingJob_ThenItStartsPending()
    {
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

        var job = BackgroundJob.Create("import", "{}", "request-42", "correlation-7", now);

        Assert.Equal(BackgroundJobState.Pending, job.State);
        Assert.Equal(now, job.CreatedAt);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
    }

    [UnitFact]
    public void GivenBlankIdempotencyKey_WhenCreatingJob_ThenArgumentExceptionIsThrown()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BackgroundJob.Create("import", "{}", " ", null, DateTimeOffset.UtcNow));

        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    [UnitFact]
    public void GivenPendingJob_WhenItCompletes_ThenStateAndTimestampsAdvance()
    {
        var created = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var started = created.AddMinutes(1);
        var completed = started.AddMinutes(2);
        var job = BackgroundJob.Create("import", "{}", "request-42", null, created);

        job.Start(started);
        job.Succeed(completed);

        Assert.Equal(BackgroundJobState.Succeeded, job.State);
        Assert.Equal(started, job.StartedAt);
        Assert.Equal(completed, job.CompletedAt);
    }

    [UnitFact]
    public void GivenRunningJobAfterRestart_WhenRequeued_ThenItReturnsToPending()
    {
        var job = BackgroundJob.Create("import", "{}", "request-42", null, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);

        job.Requeue();

        Assert.Equal(BackgroundJobState.Pending, job.State);
        Assert.Null(job.StartedAt);
    }

    [UnitFact]
    public void GivenHandlerFailure_WhenJobFails_ThenReasonAndCompletionAreRecorded()
    {
        var job = BackgroundJob.Create("import", "{}", "request-42", null, DateTimeOffset.UtcNow);
        var completed = DateTimeOffset.UtcNow.AddMinutes(1);
        job.Start(DateTimeOffset.UtcNow);

        job.Fail("invalid document", completed);

        Assert.Equal(BackgroundJobState.Failed, job.State);
        Assert.Equal("invalid document", job.FailureReason);
        Assert.Equal(completed, job.CompletedAt);
    }

    [UnitFact]
    public void GivenBlankFailureReason_WhenJobFails_ThenSafeFallbackIsRecorded()
    {
        var job = BackgroundJob.Create("import", "{}", "request-42", null, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);

        job.Fail(" ", DateTimeOffset.UtcNow);

        Assert.Equal("The job failed.", job.FailureReason);
    }

    [UnitFact]
    public void GivenCompletedJob_WhenRequeued_ThenInvalidOperationExceptionIsThrown()
    {
        var job = BackgroundJob.Create("import", "{}", "request-42", null, DateTimeOffset.UtcNow);
        job.Start(DateTimeOffset.UtcNow);
        job.Succeed(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(job.Requeue);
    }

    [UnitFact]
    public void GivenPendingJob_WhenSucceededWithoutStarting_ThenInvalidOperationExceptionIsThrown()
    {
        var job = BackgroundJob.Create("import", "{}", "request-42", null, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => job.Succeed(DateTimeOffset.UtcNow));
    }

    [UnitFact]
    public void GivenNullPayload_WhenCreatingJob_ThenArgumentNullExceptionIsThrown()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            BackgroundJob.Create("import", null!, "request-42", null, DateTimeOffset.UtcNow));

        Assert.Equal("payload", exception.ParamName);
    }
}
