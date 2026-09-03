using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Mediator.Query;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/me")]
public sealed class MeController(
    QueryMediator queryMediator,
    IRequestActorAccessor actorAccessor) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [UserProfileMessages.ProfileNotFound] = StatusCodes.Status404NotFound
        };

    [HttpGet]
    public async Task<ActionResult<DataOutput<UserProfileOutput?>>> Get()
    {
        var query = new GetMyProfileQuery
        {
            ExternalSubject = actorAccessor.Actor!.SubjectId
        };
        var result = await queryMediator.ExecuteQueryAsync<GetMyProfileQuery, UserProfileOutput>(query);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
