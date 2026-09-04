using ArturRios.Fortuna.Domain.Auditing;
using ArturRios.Mediator.Query;

namespace ArturRios.Fortuna.Query.Input;

public sealed class ListAuditEntriesQuery : BaseQuery
{
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Operation { get; set; }
    public AuditOutcome? Outcome { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}
