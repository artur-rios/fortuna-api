using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using ArturRios.Fortuna.WebApi.Filters;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/connections")]
public sealed class ConnectionsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [ConnectionMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [ConnectionMessages.Duplicate] = StatusCodes.Status409Conflict,
            [ConnectionMessages.InvalidReference] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.SourceUnavailable] = StatusCodes.Status503ServiceUnavailable,
            [ConnectionMessages.SourceNotAvailable] = StatusCodes.Status503ServiceUnavailable,
            [ConnectionMessages.DataSourceRequired] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.DataSourceInvalid] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.ExternalReferenceRequired] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.ExternalReferenceInvalid] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.BankCredentialRejected] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.NotFound] = StatusCodes.Status404NotFound,
            [ConnectionMessages.ReauthenticationNotRequired] = StatusCodes.Status409Conflict,
            [ConnectionMessages.Revoked] = StatusCodes.Status409Conflict,
            [ConnectionMessages.DuplicateReference] = StatusCodes.Status409Conflict,
            [ConnectionMessages.InvalidPageNumber] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.InvalidPageSize] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.SortByUnsupported] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.DataSourceTypeInvalid] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.StatusInvalid] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateConnectionCommandOutput?>>> Create(
        [FromBody] CreateConnectionCommand command)
    {
        return await SendAsync<
            CreateConnectionCommand,
            CreateConnectionCommandOutput>(command);
    }

    [HttpPost("{id:guid}/reauthenticate")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ReauthenticateConnectionCommandOutput?>>> Reauthenticate(
        Guid id,
        [FromBody] ReauthenticateConnectionCommand command)
    {
        command.Id = id;

        return await SendAsync<
            ReauthenticateConnectionCommand,
            ReauthenticateConnectionCommandOutput>(command);
    }

    [HttpPost("{id:guid}/revoke")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RevokeConnectionCommandOutput?>>> Revoke(Guid id)
    {
        return await SendAsync<
            RevokeConnectionCommand,
            RevokeConnectionCommandOutput>(new RevokeConnectionCommand { Id = id });
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ConnectionOutput?>>> GetById(Guid id)
    {
        return await QueryAsync<
            GetConnectionByIdQuery,
            ConnectionOutput>(new GetConnectionByIdQuery { Id = id });
    }

    [HttpGet]
    [AllowedQuery("PageNumber", "PageSize", "DataSourceType", "Status", "SortBy", "Descending")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<ConnectionOutput>>> List(
        [FromQuery] ListConnectionsQuery query)
    {
        return await QueryPageAsync<
            ListConnectionsQuery,
            ConnectionOutput>(query);
    }
}
