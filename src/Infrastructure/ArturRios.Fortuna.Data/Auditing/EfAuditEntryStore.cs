using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Shared.Auditing;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Auditing;

public sealed class EfAuditEntryStore(AppDbContext context) : IAuditEntryStore, IAuditEntryReader
{
    public IQueryable<AuditEntry> Query() => context.AuditEntries.AsNoTracking();

    public async Task AppendAsync(AuditEntryWrite entry, CancellationToken cancellationToken)
    {
        Guid? actorUserId = null;
        if (entry.ActorSubjectId.HasValue)
        {
            var subjectId = entry.ActorSubjectId.Value;
            var actor = entry.ActorIsLocal
                ? await context.UserProfiles.SingleOrDefaultAsync(
                    profile => profile.PublicId == subjectId,
                    cancellationToken)
                : await context.UserProfiles.SingleOrDefaultAsync(
                    profile => profile.ExternalSubject == subjectId.ToString("D"),
                    cancellationToken);
            actorUserId = actor?.PublicId;
        }

        context.AuditEntries.Add(new AuditEntry(
            actorUserId,
            entry.Operation,
            entry.EntityType,
            entry.EntityPublicId,
            entry.Outcome,
            entry.Reason,
            entry.OccurredAt));
        await context.SaveChangesAsync(cancellationToken);
    }
}
