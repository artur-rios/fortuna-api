using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Shared.Jobs;
using ArturRios.Fortuna.WebApi.Services;
using ArturRios.Output;
using ArturRios.Util.Test.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class BackgroundJobHostedServiceTests
{
    [UnitTheory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 8)]
    [InlineData(7, 60)]
    [InlineData(1000, 60)]
    public void GivenConsecutiveFailures_WhenBackoffIsComputed_ThenItDoublesUpToTheCap(
        int failures,
        int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), BackgroundJobHostedService.Backoff(failures));
    }

    [UnitFact]
    public async Task GivenTransientStoreFailure_WhenJobIsProcessed_ThenHostKeepsRunningAndRetriesTheJob()
    {
        var job = BackgroundJob.Create("probe", "{}", "probe-1", null, DateTimeOffset.UtcNow);
        var store = new FlakyStore(job, failures: 1);
        var handler = new RecordingHandler();
        var queue = new BackgroundJobQueue(4);
        using var service = Service(queue, store, handler);

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(job.Id, CancellationToken.None);
        await handler.Executed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, store.FindCount);
        Assert.Equal(BackgroundJobState.Succeeded, job.State);
    }

    [UnitFact]
    public async Task GivenRecoveryFailsOnce_WhenServiceStarts_ThenRecoveryIsRetriedAndJobsAreProcessed()
    {
        var job = BackgroundJob.Create("probe", "{}", "probe-2", null, DateTimeOffset.UtcNow);
        var store = new FlakyStore(job, failures: 0, recoveryFailures: 1);
        var handler = new RecordingHandler();
        using var service = Service(new BackgroundJobQueue(4), store, handler);

        await service.StartAsync(CancellationToken.None);
        await handler.Executed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, store.RecoverCount);
        Assert.Equal(BackgroundJobState.Succeeded, job.State);
    }

    private static BackgroundJobHostedService Service(
        IBackgroundJobQueue queue,
        IBackgroundJobStore store,
        IBackgroundJobHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(store);
        services.AddSingleton(handler);
        services.AddScoped<BackgroundJobProcessor>();
        var provider = services.BuildServiceProvider();

        return new BackgroundJobHostedService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<BackgroundJobHostedService>.Instance);
    }

    private sealed class RecordingHandler : IBackgroundJobHandler
    {
        public TaskCompletionSource Executed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string JobType => "probe";

        public Task<ProcessOutput> ExecuteAsync(string payload, CancellationToken cancellationToken)
        {
            Executed.TrySetResult();

            return Task.FromResult(ProcessOutput.New);
        }
    }

    private sealed class FlakyStore(BackgroundJob job, int failures, int recoveryFailures = 0)
        : IBackgroundJobStore
    {
        private int remainingFailures = failures;
        private int remainingRecoveryFailures = recoveryFailures;

        public int FindCount { get; private set; }
        public int RecoverCount { get; private set; }

        public Task<BackgroundJob> CreateAsync(
            string type,
            string payload,
            string idempotencyKey,
            string? correlationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BackgroundJob?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            FindCount++;
            if (remainingFailures-- > 0)
            {
                return Task.FromException<BackgroundJob?>(new IOException("database unavailable"));
            }

            return Task.FromResult<BackgroundJob?>(id == job.Id ? job : null);
        }

        public Task<BackgroundJob?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BackgroundJob?> FindActiveAsync(
            string type,
            string idempotencyKeyPrefix,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<BackgroundJob>> RecoverAsync(CancellationToken cancellationToken)
        {
            RecoverCount++;
            if (remainingRecoveryFailures-- > 0)
            {
                return Task.FromException<IReadOnlyList<BackgroundJob>>(new IOException("database unavailable"));
            }

            return Task.FromResult<IReadOnlyList<BackgroundJob>>(recoveryFailures > 0 ? [job] : []);
        }

        public Task SaveAsync(BackgroundJob changedJob, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
