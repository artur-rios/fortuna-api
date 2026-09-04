using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Shared.Auditing;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Auditing;

public sealed class EfAuditEntryStore(AppDbContext context) : IAuditEntryStore
{
    public async Task AppendAsync(AuditEntryWrite entry, CancellationToken cancellationToken)
    {
        UserProfile? user = null;
        if (entry.ActorSubjectId.HasValue)
        {
            var subjectId = entry.ActorSubjectId.Value;
            user = entry.ActorIsLocal
                ? await context.UserProfiles.SingleOrDefaultAsync(
                    profile => profile.PublicId == subjectId,
                    cancellationToken)
                : await context.UserProfiles.SingleOrDefaultAsync(
                    profile => profile.ExternalSubject == subjectId.ToString("D"),
                    cancellationToken);
        }

        context.AuditEntries.Add(new AuditEntry(
            user,
            entry.Operation,
            entry.EntityType,
            entry.EntityPublicId,
            entry.Outcome,
            entry.Reason,
            entry.OccurredAt));
        await context.SaveChangesAsync(cancellationToken);
    }
}
