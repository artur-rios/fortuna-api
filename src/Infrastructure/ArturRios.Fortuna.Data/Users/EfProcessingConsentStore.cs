using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Users;

public sealed class EfProcessingConsentStore(AppDbContext context) : IProcessingConsentStore
{
    public async Task<IReadOnlyCollection<ProcessingConsentSnapshot>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken) => await context.ProcessingConsents
        .AsNoTracking()
        .Where(item => item.User.PublicId == userId)
        .OrderBy(item => item.Purpose)
        .Select(item => new ProcessingConsentSnapshot(
            item.PublicId,
            item.Purpose,
            item.Version,
            item.GrantedAt,
            item.UpdatedAt))
        .ToArrayAsync(cancellationToken);

    public Task<bool> IsCurrentAsync(
        Guid userId,
        ProcessingConsentPurpose purpose,
        string version,
        CancellationToken cancellationToken) => context.ProcessingConsents
        .AsNoTracking()
        .AnyAsync(item => item.User.PublicId == userId &&
                          item.Purpose == purpose &&
                          item.Version == version,
            cancellationToken);

    public async Task<ProcessingConsentSnapshot> GrantAsync(
        Guid userId,
        ProcessingConsentPurpose purpose,
        string version,
        DateTimeOffset grantedAt,
        CancellationToken cancellationToken)
    {
        var user = await context.UserProfiles.SingleAsync(
            item => item.PublicId == userId,
            cancellationToken);
        var consent = await context.ProcessingConsents.SingleOrDefaultAsync(
            item => item.UserId == user.Id && item.Purpose == purpose,
            cancellationToken);
        if (consent is null)
        {
            consent = new ProcessingConsent(user, purpose, version, grantedAt);
            context.ProcessingConsents.Add(consent);
        }
        else
        {
            consent.Grant(version, grantedAt);
        }
        await context.SaveChangesAsync(cancellationToken);
        return Snapshot(consent);
    }

    public async Task<ProcessingConsentWithdrawal> WithdrawAsync(
        Guid userId,
        ProcessingConsentPurpose purpose,
        DateTimeOffset withdrawnAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await context.UserProfiles.SingleOrDefaultAsync(
            item => item.PublicId == userId,
            cancellationToken);
        var consent = user is null
            ? null
            : await context.ProcessingConsents.SingleOrDefaultAsync(
                item => item.UserId == user.Id && item.Purpose == purpose,
                cancellationToken);
        if (consent is null)
        {
            return new ProcessingConsentWithdrawal(false, 0, 0);
        }

        var connections = await context.Connections.Where(item =>
                item.UserId == user!.Id &&
                item.DataSourceType == Domain.Transactions.TransactionSourceType.Pluggy &&
                item.Status != ConnectionStatus.Revoked)
            .ToArrayAsync(cancellationToken);
        foreach (var connection in connections)
        {
            connection.Revoke(withdrawnAt);
        }
        var connectionIds = connections.Select(item => item.Id).ToArray();
        var jobs = connectionIds.Length == 0
            ? []
            : await context.ImportJobs.Where(job =>
                    job.ConnectionId.HasValue && connectionIds.Contains(job.ConnectionId.Value) &&
                    (job.Status == ImportJobStatus.Pending || job.Status == ImportJobStatus.Running))
                .ToArrayAsync(cancellationToken);
        foreach (var job in jobs)
        {
            job.Fail(ConnectionMessages.SynchronizationStoppedByRevocation, withdrawnAt);
        }

        context.ProcessingConsents.Remove(consent);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProcessingConsentWithdrawal(true, connections.Length, jobs.Length);
    }

    private static ProcessingConsentSnapshot Snapshot(ProcessingConsent item) => new(
        item.PublicId,
        item.Purpose,
        item.Version,
        item.GrantedAt,
        item.UpdatedAt);
}
