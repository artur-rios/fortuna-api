using ArturRios.Fortuna.Domain.Auditing;

namespace ArturRios.Fortuna.Shared.Auditing;

public interface IAuditEntryReader
{
    IQueryable<AuditEntry> Query();
}
