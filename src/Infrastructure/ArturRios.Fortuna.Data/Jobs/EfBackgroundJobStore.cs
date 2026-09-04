using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Jobs;
using ArturRios.Fortuna.Shared.Jobs;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Jobs;

public sealed class EfBackgroundJobStore(AppDbContext context) : IBackgroundJobStore
{
    public async Task<BackgroundJob> CreateAsync(
        string type,
        string payload,
        string idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var existing = await context.BackgroundJobs.SingleOrDefaultAsync(
            x => x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var job = BackgroundJob.Create(type, payload, idempotencyKey, correlationId, DateTimeOffset.UtcNow);
        context.BackgroundJobs.Add(job);
        await context.SaveChangesAsync(cancellationToken);
        return job;
    }

    public Task<BackgroundJob?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.BackgroundJobs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<BackgroundJob?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        context.BackgroundJobs.SingleOrDefaultAsync(
            x => x.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task<IReadOnlyList<BackgroundJob>> RecoverAsync(CancellationToken cancellationToken)
    {
        var jobs = await context.BackgroundJobs
            .Where(x => x.State == BackgroundJobState.Pending || x.State == BackgroundJobState.Running)
            .ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            job.Requeue();
        }

        await context.SaveChangesAsync(cancellationToken);
        return jobs;
    }

    public Task SaveAsync(BackgroundJob job, CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
