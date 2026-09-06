using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/data-sources")]
public sealed class DataSourcesController(QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [DataSourceMessages.ListedSuccessfully] = StatusCodes.Status200OK
        };

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<DataSourceListOutput?>>> List()
    {
        var result = await queryMediator.ExecuteQueryAsync<
            ListDataSourcesQuery,
            DataSourceListOutput>(new ListDataSourcesQuery());
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
