using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListConnectionsQueryHandler(
    IValidator<ListConnectionsQuery> validator,
    IUserProfileReader profiles,
    IConnectionReader connections,
    IRequestActorAccessor actorAccessor,
    PaginationOptions paginationOptions)
    : IPaginatedQueryHandlerAsync<ListConnectionsQuery, ConnectionOutput>
{
    public async Task<PaginatedOutput<ConnectionOutput>> HandleAsync(ListConnectionsQuery query)
    {
        var output = PaginatedOutput<ConnectionOutput>.New;
        var validation = await validator.ValidateAsync(query);
        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(error => error.ErrorMessage));
        }

        var profile = await ResolveProfileAsync(actorAccessor.Actor);
        if (profile is null)
        {
            return output.WithError(ConnectionMessages.ProfileNotFound);
        }

        var filtered = connections.Query().Where(item => item.User.PublicId == profile.Id);
        if (query.DataSourceType.HasValue)
        {
            filtered = filtered.Where(item => item.DataSourceType == query.DataSourceType.Value);
        }

        if (query.Status.HasValue)
        {
            filtered = filtered.Where(item => item.Status == query.Status.Value);
        }

        var ordered = Order(filtered, query.SortBy.Trim(), query.Descending);
        var projected = ordered.Select(item => new ConnectionOutput
        {
            Id = item.PublicId,
            DataSourceType = item.DataSourceType,
            ExternalReference = item.ExternalReference,
            Status = item.Status,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        });
        var pageSize = Math.Min(query.PageSize, paginationOptions.MaximumPageSize);
        var page = await projected.PaginateAsync(
            query.PageNumber,
            pageSize,
            orderBy: null,
            cancellationToken: CancellationToken.None);
        return page.WithMessage(ConnectionMessages.ListedSuccessfully);
    }

    private async Task<UserProfileSnapshot?> ResolveProfileAsync(RequestActor? actor) =>
        actor?.IsLocal == true
            ? await profiles.FindByPublicIdAsync(actor.SubjectId, CancellationToken.None)
            : actor is null
                ? null
                : await profiles.FindByExternalSubjectAsync(actor.SubjectId, CancellationToken.None);

    private static IOrderedQueryable<Connection> Order(
        IQueryable<Connection> connections,
        string sortBy,
        bool descending) => (sortBy.ToLowerInvariant(), descending) switch
        {
            ("datasourcetype", false) => connections.OrderBy(item => item.DataSourceType)
                .ThenBy(item => item.PublicId),
            ("datasourcetype", true) => connections.OrderByDescending(item => item.DataSourceType)
                .ThenByDescending(item => item.PublicId),
            ("status", false) => connections.OrderBy(item => item.Status)
                .ThenBy(item => item.PublicId),
            ("status", true) => connections.OrderByDescending(item => item.Status)
                .ThenByDescending(item => item.PublicId),
            ("updatedat", false) => connections.OrderBy(item => item.UpdatedAt)
                .ThenBy(item => item.PublicId),
            ("updatedat", true) => connections.OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.PublicId),
            (_, false) => connections.OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.PublicId),
            _ => connections.OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.PublicId)
        };
}
