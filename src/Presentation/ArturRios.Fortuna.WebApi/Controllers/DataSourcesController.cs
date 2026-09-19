using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/data-sources")]
public sealed class DataSourcesController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [DataSourceMessages.ListedSuccessfully] = StatusCodes.Status200OK
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<DataSourceListOutput?>>> List()
    {
        return await QueryAsync<
            ListDataSourcesQuery,
            DataSourceListOutput>(new ListDataSourcesQuery());
    }
}
