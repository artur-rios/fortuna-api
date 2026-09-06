using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Shared.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Ingestion;

public sealed class EfImportJobReader(AppDbContext context) : IImportJobReader
{
    public IQueryable<ImportJob> Query() => context.ImportJobs.AsNoTracking();

    public IQueryable<ImportedRecord> Records() => context.ImportedRecords.AsNoTracking();

    public Task<ImportJobReadSnapshot?> FindByIdAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => context.ImportJobs.AsNoTracking()
        .Where(item => item.User.PublicId == userId && item.PublicId == id)
        .Select(item => new ImportJobReadSnapshot(
            item.PublicId,
            item.Connection == null ? null : item.Connection.PublicId,
            item.SourceType,
            item.Status,
            item.PeriodStart,
            item.PeriodEnd,
            item.ImportedCount,
            item.DuplicateCount,
            item.RejectedCount,
            item.FailureReason,
            item.CreatedAt,
            item.UpdatedAt))
        .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> IsOwnedAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) => context.ImportJobs.AsNoTracking().AnyAsync(item =>
            item.User.PublicId == userId && item.PublicId == id,
            cancellationToken);
}
