using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/connections")]
public sealed class ConnectionsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly HashSet<string> ListQueryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PageNumber",
        "PageSize",
        "DataSourceType",
        "Status",
        "SortBy",
        "Descending"
    };

    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [ConnectionMessages.CreatedSuccessfully] = StatusCodes.Status201Created,
            [ConnectionMessages.Duplicate] = StatusCodes.Status409Conflict,
            [ConnectionMessages.InvalidReference] = StatusCodes.Status400BadRequest,
            [ConnectionMessages.SourceUnavailable] = StatusCodes.Status503ServiceUnavailable,
            [ConnectionMessages.SourceNotAvailable] = StatusCodes.Status404NotFound,
            [ConnectionMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
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
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<CreateConnectionCommandOutput?>>> Create(
        [FromBody] CreateConnectionCommand command)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            CreateConnectionCommand,
            CreateConnectionCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/reauthenticate")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ReauthenticateConnectionCommandOutput?>>> Reauthenticate(
        Guid id,
        [FromBody] ReauthenticateConnectionCommand command)
    {
        command.Id = id;
        var result = await commandMediator.ExecuteCommandAsync<
            ReauthenticateConnectionCommand,
            ReauthenticateConnectionCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("{id:guid}/revoke")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<RevokeConnectionCommandOutput?>>> Revoke(Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            RevokeConnectionCommand,
            RevokeConnectionCommandOutput>(new RevokeConnectionCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ConnectionOutput?>>> GetById(Guid id)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetConnectionByIdQuery,
            ConnectionOutput>(new GetConnectionByIdQuery { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<PaginatedOutput<ConnectionOutput>>> List(
        [FromQuery] ListConnectionsQuery query)
    {
        var unsupported = Request.Query.Keys.FirstOrDefault(key => !ListQueryFields.Contains(key));
        if (unsupported is not null)
        {
            return BadRequest(PaginatedOutput<ConnectionOutput>.New.WithError(
                ConnectionMessages.UnsupportedFilter(unsupported)));
        }

        var result = await queryMediator.ExecutePaginatedQueryAsync<
            ListConnectionsQuery,
            ConnectionOutput>(query);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
