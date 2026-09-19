using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Mediator.Query.Interfaces;
using ArturRios.Output;

namespace ArturRios.Fortuna.Query.Handlers;

public sealed class GetConnectionByIdQueryHandler(
    ICurrentProfileResolver profileResolver,
    IConnectionReader connections)
    : IQueryHandlerAsync<GetConnectionByIdQuery, ConnectionOutput>
{
    public async Task<DataOutput<ConnectionOutput?>> HandleAsync(GetConnectionByIdQuery query)
    {
        var output = DataOutput<ConnectionOutput?>.New;
        var profile = await profileResolver.ResolveAsync();
        if (profile is null)
        {
            return output.WithError(ConnectionMessages.ProfileNotFound);
        }

        var connection = await connections.FindByIdAsync(
            profile.Id, query.Id, CancellationToken.None);

        return connection is null
            ? output.WithError(ConnectionMessages.NotFound)
            : output.WithData(Project(connection)).WithMessage(
                ConnectionMessages.RetrievedSuccessfully);
    }

    internal static ConnectionOutput Project(ConnectionSnapshot connection) => new()
    {
        Id = connection.Id,
        DataSourceType = connection.DataSourceType,
        ExternalReference = connection.ExternalReference,
        Status = connection.Status,
        CreatedAt = connection.CreatedAt,
        UpdatedAt = connection.UpdatedAt
    };
}
