using ArturRios.Mediator.Command;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

/// <summary>
///     Base for Fortuna's API controllers: dispatches commands and queries through the request's
///     mediators and turns their envelopes into responses with the controller's status map, which
///     is built over <see cref="FortunaStatusMap.Shared" />. An action with its own map passes it
///     explicitly.
/// </summary>
public abstract class FortunaController : Controller
{
    /// <summary>The controller's message-to-status map, merged over the shared entries.</summary>
    protected virtual IReadOnlyDictionary<string, int> StatusMap => FortunaStatusMap.Shared;

    protected CommandMediator Commands =>
        HttpContext.RequestServices.GetRequiredService<CommandMediator>();

    protected QueryMediator Queries =>
        HttpContext.RequestServices.GetRequiredService<QueryMediator>();

    protected async Task<ActionResult<DataOutput<TOutput?>>> SendAsync<TCommand, TOutput>(
        TCommand command,
        IReadOnlyDictionary<string, int>? statusMap = null)
        where TCommand : BaseCommand
        where TOutput : CommandOutput =>
        Respond(await Commands.ExecuteCommandAsync<TCommand, TOutput>(command), statusMap);

    protected async Task<ActionResult<DataOutput<TOutput?>>> QueryAsync<TQuery, TOutput>(
        TQuery query,
        IReadOnlyDictionary<string, int>? statusMap = null)
        where TQuery : BaseQuery
        where TOutput : QueryOutput =>
        Respond(await Queries.ExecuteQueryAsync<TQuery, TOutput>(query), statusMap);

    protected async Task<ActionResult<PaginatedOutput<TOutput>>> QueryPageAsync<TQuery, TOutput>(
        TQuery query)
        where TQuery : BaseQuery
        where TOutput : QueryOutput =>
        Respond(await Queries.ExecutePaginatedQueryAsync<TQuery, TOutput>(query));

    /// <summary>Resolves an envelope with <paramref name="statusMap" />, or the controller's map.</summary>
    protected ActionResult<DataOutput<T?>> Respond<T>(
        DataOutput<T?> result,
        IReadOnlyDictionary<string, int>? statusMap = null) =>
        ResponseResolver.Resolve(result, statusMap: statusMap ?? StatusMap);

    /// <summary>Resolves a page with the controller's map.</summary>
    protected ActionResult<PaginatedOutput<T>> Respond<T>(PaginatedOutput<T> result) =>
        ResponseResolver.Resolve(result, statusMap: StatusMap);
}
