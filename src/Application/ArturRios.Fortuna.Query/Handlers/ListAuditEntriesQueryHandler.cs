using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Auditing;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListAuditEntriesQueryHandler(
    IValidator<ListAuditEntriesQuery> validator,
    IUserProfileReader profiles,
    IAuditEntryReader entries,
    IRequestActorAccessor actorAccessor)
    : IPaginatedQueryHandlerAsync<ListAuditEntriesQuery, AuditEntryOutput>
{
    public async Task<PaginatedOutput<AuditEntryOutput>> HandleAsync(ListAuditEntriesQuery query)
    {
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return PaginatedOutput<AuditEntryOutput>.New
                .WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var actor = actorAccessor.Actor;
        var profile = actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);
        if (profile is null)
        {
            return PaginatedOutput<AuditEntryOutput>.New
                .WithError(AuditEntryMessages.ProfileNotFound);
        }

        var filtered = entries.Query().Where(entry => entry.ActorUserId == profile.Id);

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
            var to = query.To.Value;
            filtered = filtered.Where(entry => entry.OccurredAt <= to);
        }

        var projected = filtered
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new AuditEntryOutput
            {
                ActorUserId = entry.ActorUserId!.Value,
                Operation = entry.Operation,
                EntityType = entry.EntityType,
                EntityId = entry.EntityPublicId,
                Outcome = entry.Outcome,
                Reason = entry.Reason,
                OccurredAt = entry.OccurredAt
            });
        var output = await projected.PaginateAsync(
            query.PageNumber,
            query.PageSize,
            orderBy: null,
            cancellationToken: CancellationToken.None);

        return output.WithMessage(AuditEntryMessages.RetrievedSuccessfully);
    }
}
