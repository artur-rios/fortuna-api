using ArturRios.Fortuna.Data.Configuration;
using ArturRios.Fortuna.Data.EntityMaps;
using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Shared.Auditing;
using Microsoft.EntityFrameworkCore;

namespace ArturRios.Fortuna.Data.Auditing;

public sealed class EfAuditEntryStore(AppDbContext context) : IAuditEntryStore, IAuditEntryReader
{
    public IQueryable<AuditEntry> Query() => context.AuditEntries.AsNoTracking();

    public async Task AppendAsync(AuditEntryWrite entry, CancellationToken cancellationToken)
    {
        Guid? subjectReference = null;
        long? actorId = null;
        var createdSubject = false;
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
            if (actor is not null)
            {
                actorId = actor.Id;
                var subject = await context.AuditSubjects.SingleOrDefaultAsync(
                    item => item.UserId == actor.Id,
                    cancellationToken);
                if (subject is null)
                {
                    subject = new AuditSubject(actor);
                    context.AuditSubjects.Add(subject);
                    createdSubject = true;
                }

                subjectReference = subject.SubjectReference;
            }
        }

        AddEntry(entry, subjectReference);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            createdSubject && actorId.HasValue &&
            DatabaseException.IsUniqueViolation(exception, AuditSubjectMap.UserIndex))
        {
            context.ChangeTracker.Clear();
            subjectReference = await context.AuditSubjects
                .Where(subject => subject.UserId == actorId.Value)
                .Select(subject => (Guid?)subject.SubjectReference)
                .SingleAsync(cancellationToken);
            AddEntry(entry, subjectReference);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<Guid?> FindSubjectReferenceAsync(
        Guid userId,
        CancellationToken cancellationToken) => context.AuditSubjects
        .AsNoTracking()
        .Where(subject => subject.User.PublicId == userId)
        .Select(subject => (Guid?)subject.SubjectReference)
        .SingleOrDefaultAsync(cancellationToken);

    private void AddEntry(AuditEntryWrite entry, Guid? subjectReference) =>
        context.AuditEntries.Add(new AuditEntry(
            subjectReference,
            entry.Operation,
            entry.EntityType,
            entry.EntityPublicId,
            entry.Outcome,
            entry.Reason,
            entry.OccurredAt));
}
