using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListAuditEntriesQueryHandler(
    ICurrentProfileResolver profileResolver,
    IAuditEntryReader entries,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListAuditEntriesQuery, AuditEntryOutput>
{
    public async Task<PaginatedOutput<AuditEntryOutput>> HandleAsync(ListAuditEntriesQuery query)
    {
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return PaginatedOutput<AuditEntryOutput>.New
                .WithError(AuditEntryMessages.ProfileNotFound);
        }

        var subjectReference = await entries.FindSubjectReferenceAsync(
            profile.Id,
            CancellationToken.None);
        var filtered = entries.Query().Where(entry =>
            subjectReference.HasValue && entry.SubjectReference == subjectReference);

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim().ToLowerInvariant();
            filtered = filtered.Where(entry =>
                entry.EntityType != null && entry.EntityType.ToLower() == entityType);
        }

        if (query.EntityId.HasValue)
        {
            var entityId = query.EntityId.Value;
            filtered = filtered.Where(entry => entry.EntityPublicId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Operation))
        {
            var operation = query.Operation.Trim().ToLowerInvariant();
            filtered = filtered.Where(entry => entry.Operation.ToLower() == operation);
        }

        if (query.Outcome.HasValue)
        {
            var outcome = query.Outcome.Value;
            filtered = filtered.Where(entry => entry.Outcome == outcome);
        }

        if (query.From.HasValue)
        {
            var from = query.From.Value;
            filtered = filtered.Where(entry => entry.OccurredAt >= from);
        }

        if (query.To.HasValue)
        {
            // A bare date arrives as midnight; it covers that whole day.
            var to = query.To.Value;
            if (to.TimeOfDay == TimeSpan.Zero)
            {
                var nextDay = to.AddDays(1);
                filtered = filtered.Where(entry => entry.OccurredAt < nextDay);
            }
            else
            {
                filtered = filtered.Where(entry => entry.OccurredAt <= to);
            }
        }

        var projected = filtered
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new AuditEntryOutput
            {
                SubjectReference = entry.SubjectReference!.Value,
                Operation = entry.Operation,
                EntityType = entry.EntityType,
                EntityId = entry.EntityPublicId,
                Outcome = entry.Outcome,
                Reason = entry.Reason,
                OccurredAt = entry.OccurredAt
            });
        var output = await projected.PaginateAsync(
            query.PageNumber,
            Math.Min(query.PageSize, paginationOptions.MaximumPageSize),
            orderBy: null,
            cancellationToken: CancellationToken.None);

        return output.WithMessage(AuditEntryMessages.RetrievedSuccessfully);
    }
}
