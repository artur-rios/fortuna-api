using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Mediator.Query;
using ArturRios.Mediator.Command;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using ArturRios.Util.WebApi.Security.Attributes;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/me")]
public sealed class MeController(
    CommandMediator commandMediator,
    QueryMediator queryMediator,
    IRequestActorAccessor actorAccessor) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [UserProfileMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [UserErasureMessages.ConfirmationInvalid] = StatusCodes.Status400BadRequest,
            [UserErasureMessages.UserNotFound] = StatusCodes.Status404NotFound,
            [PersonalDataExportMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [PersonalDataExportMessages.NotFound] = StatusCodes.Status404NotFound,
            [PersonalDataExportMessages.Expired] = StatusCodes.Status404NotFound,
            [PersonalDataExportMessages.FileNotFound] = StatusCodes.Status404NotFound,
            [PersonalDataExportMessages.StorageUnavailable] = StatusCodes.Status503ServiceUnavailable
        };

    [HttpGet]
    public async Task<ActionResult<DataOutput<UserProfileOutput?>>> Get()
    {
        var query = new GetMyProfileQuery
        {
            ExternalSubject = actorAccessor.Actor!.SubjectId,
            IsLocal = actorAccessor.Actor.IsLocal
        };
        var result = await queryMediator.ExecuteQueryAsync<GetMyProfileQuery, UserProfileOutput>(query);

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("erasure")]
    public async Task<ActionResult<DataOutput<EraseUserCommandOutput?>>> Erase(
        [FromBody] EraseUserCommand command)
    {
        command.IsSelfService = true;
        var result = await commandMediator.ExecuteCommandAsync<
            EraseUserCommand,
            EraseUserCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("data-export")]
    [RoleRequirement((int)HeimdallRoles.User)]
    [ProducesResponseType(typeof(DataOutput<RequestPersonalDataExportCommandOutput?>),
        StatusCodes.Status202Accepted)]
    public async Task<ActionResult<DataOutput<RequestPersonalDataExportCommandOutput?>>> RequestDataExport()
    {
        var command = new RequestPersonalDataExportCommand
        {
            CorrelationId = HttpContext.TraceIdentifier
        };
        var result = await commandMediator.ExecuteCommandAsync<
            RequestPersonalDataExportCommand,
            RequestPersonalDataExportCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: new Dictionary<string, int>
        {
            [PersonalDataExportMessages.Accepted] = StatusCodes.Status202Accepted,
            [PersonalDataExportMessages.ProfileNotFound] = StatusCodes.Status404NotFound
        });
    }

    [HttpGet("data-export/{jobId:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DataOutput<PersonalDataExportQueryOutput?>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDataExport(Guid jobId)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetPersonalDataExportQuery,
            PersonalDataExportQueryOutput>(new GetPersonalDataExportQuery { JobId = jobId });
        if (result.Success && result.Data?.Content is not null)
        {
            return File(
                result.Data.Content,
                result.Data.ContentType!,
                result.Data.FileName,
                enableRangeProcessing: true);
        }
        var response = ResponseResolver.Resolve(result, statusMap: StatusMap);
        return response.Result ?? Ok(response.Value);
    }
}
