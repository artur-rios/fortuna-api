using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Fortuna.Query.Handlers;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Input.Validation;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Pagination;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Query.Tests;

public sealed class ConnectionQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 13, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public async Task GivenOwnedConnection_WhenRead_ThenStatusIsReturnedWithoutToken()
    {
        var user = User();
        var connection = Connection(user);
        connection.MarkRequiresReauthentication(Now.AddMinutes(1));
        var handler = GetHandler(Profile(user), new StubReader(connection));

        var result = await handler.HandleAsync(new GetConnectionByIdQuery
        {
            Id = connection.PublicId
        });

        Assert.True(result.Success);
        Assert.Equal(connection.PublicId, result.Data?.Id);
        Assert.Equal(ConnectionStatus.RequiresReauthentication, result.Data?.Status);
        Assert.Equal(connection.ExternalReference, result.Data?.ExternalReference);
        Assert.Contains(ConnectionMessages.RetrievedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenForeignOrMissingConnection_WhenRead_ThenSameNotFoundIsReturned()
    {
        var owner = User();
        var actor = User();
        var connection = Connection(owner);
        var handler = GetHandler(Profile(actor), new StubReader(connection));

        var foreign = await handler.HandleAsync(new GetConnectionByIdQuery
        {
            Id = connection.PublicId
        });
        var missing = await handler.HandleAsync(new GetConnectionByIdQuery
        {
            Id = Guid.NewGuid()
        });

        Assert.Equal(foreign.Errors, missing.Errors);
        Assert.Contains(ConnectionMessages.NotFound, foreign.Errors);
    }

    [UnitFact]
    public async Task GivenStatusFilter_WhenListed_ThenOnlyOwnedMatchesAreReturned()
    {
        var actor = User();
        var other = User();
        var active = Connection(actor, Now.AddDays(-1));
        var requires = Connection(actor, Now);
        requires.MarkRequiresReauthentication(Now.AddMinutes(1));
        var foreign = Connection(other, Now.AddDays(1));
        foreign.MarkRequiresReauthentication(Now.AddMinutes(2));
        var handler = ListHandler(
            Profile(actor), new StubReader(active, requires, foreign));

        var result = await handler.HandleAsync(new ListConnectionsQuery
        {
            Status = ConnectionStatus.RequiresReauthentication,
            SortBy = "UpdatedAt",
            Descending = true
        });

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalItems);
        Assert.Equal(requires.PublicId, result.Data?.Single().Id);
        Assert.Contains(ConnectionMessages.ListedSuccessfully, result.Messages);
    }

    [UnitFact]
    public async Task GivenInvalidListCriteria_WhenListed_ThenNamedErrorsAreReturned()
    {
        var result = await ListHandler(null, new StubReader()).HandleAsync(
            new ListConnectionsQuery
            {
                PageNumber = 0,
                PageSize = 0,
                DataSourceType = (TransactionSourceType)99,
                Status = (ConnectionStatus)99,
                SortBy = "Name"
            });

        Assert.Contains(ConnectionMessages.InvalidPageNumber, result.Errors);
        Assert.Contains(ConnectionMessages.InvalidPageSize, result.Errors);
        Assert.Contains(ConnectionMessages.DataSourceTypeInvalid, result.Errors);
        Assert.Contains(ConnectionMessages.StatusInvalid, result.Errors);
        Assert.Contains(ConnectionMessages.SortByUnsupported, result.Errors);
    }

    private static GetConnectionByIdQueryHandler GetHandler(
        UserProfileSnapshot? profile,
        IConnectionReader reader) => new(
        new StubProfileReader(profile), reader, Actor(profile));

    private static ListConnectionsQueryHandler ListHandler(
        UserProfileSnapshot? profile,
        IConnectionReader reader) => new(
        new ListConnectionsQueryValidator(),
        new StubProfileReader(profile),
        reader,
        Actor(profile),
        new PaginationOptions(100));

    private static StubActorAccessor Actor(UserProfileSnapshot? profile) => new(
        new RequestActor(profile?.ExternalSubject ?? Guid.NewGuid(), 3, null, []));

    private static UserProfile User() => new(
        Guid.NewGuid(), "Owner", new Currency("BRL", "Brazilian real", 2), Now);

    private static Connection Connection(UserProfile user, DateTimeOffset? createdAt = null) => new(
        user,
        TransactionSourceType.Pluggy,
        Guid.NewGuid().ToString(),
        [1, 2, 3],
        createdAt ?? Now);

    private static UserProfileSnapshot Profile(UserProfile user) => new(
        user.PublicId,
        Guid.Parse(user.ExternalSubject!),
        user.DisplayName,
        user.DisplayCurrency.Code,
        false,
        user.CreatedAt,
        user.UpdatedAt);

    private sealed class StubReader(params Connection[] connections) : IConnectionReader
    {
        public IQueryable<Connection> Query() => connections.AsQueryable();

        public Task<ConnectionSnapshot?> FindByIdAsync(
            Guid userId, Guid id, CancellationToken cancellationToken)
        {
            var connection = connections.SingleOrDefault(item =>
                item.User.PublicId == userId && item.PublicId == id);
            return Task.FromResult(connection is null ? null : new ConnectionSnapshot(
                connection.PublicId,
                connection.DataSourceType,
                connection.ExternalReference,
                connection.Status,
                connection.CreatedAt,
                connection.UpdatedAt));
        }
    }

    private sealed class StubProfileReader(UserProfileSnapshot? profile) : IUserProfileReader
    {
        public Task<UserProfileSnapshot?> FindByExternalSubjectAsync(
            Guid externalSubject, CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task<UserProfileSnapshot?> FindByPublicIdAsync(
            Guid publicId, CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class StubActorAccessor(RequestActor? actor) : IRequestActorAccessor
    {
        public RequestActor? Actor => actor;
    }
}
