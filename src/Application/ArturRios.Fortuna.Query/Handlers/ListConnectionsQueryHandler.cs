using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class ListConnectionsQueryHandler(
    IValidator<ListConnectionsQuery> validator,
    ICurrentProfileResolver profileResolver,
    IConnectionReader connections,
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

        var profile = await profileResolver.ResolveAsync();
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

    private static IOrderedQueryable<Connection> Order(
        IQueryable<Connection> connections,
        string sortBy,
        bool descending) => sortBy.ToLowerInvariant() switch
        {
            "datasourcetype" => connections
                .SortBy(item => item.DataSourceType, item => item.PublicId, descending),
            "status" => connections.SortBy(item => item.Status, item => item.PublicId, descending),
            "updatedat" => connections
                .SortBy(item => item.UpdatedAt, item => item.PublicId, descending),
            _ => connections.SortBy(item => item.CreatedAt, item => item.PublicId, descending)
        };
}
