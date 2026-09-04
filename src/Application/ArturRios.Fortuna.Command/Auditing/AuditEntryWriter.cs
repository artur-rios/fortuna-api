using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Security;

namespace ArturRios.Fortuna.Command.Auditing;

public sealed class AuditEntryWriter(
    IAuditEntryStore entries,
    IRequestActorAccessor actorAccessor,
    TimeProvider timeProvider) : IAuditEntryWriter
{
    private const int MaxReasonLength = 1000;

    public Task WriteAsync(
        string operation,
        string? entityType,
        Guid? entityPublicId,
        bool succeeded,
        string? reason)
    {
        var actor = actorAccessor.Actor;
        var safeReason = reason is { Length: > MaxReasonLength }
            ? reason[..MaxReasonLength]
            : reason;

        return entries.AppendAsync(
            new AuditEntryWrite(
                actor?.SubjectId,
                actor?.IsLocal == true,
                operation,
                entityType,
                entityPublicId,
                succeeded ? AuditOutcome.Succeeded : AuditOutcome.Refused,
                succeeded ? null : safeReason,
                timeProvider.GetUtcNow()),
            CancellationToken.None);
    }
}
