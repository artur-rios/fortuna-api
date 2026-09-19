using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using Microsoft.AspNetCore.Mvc;
using ArturRios.Util.WebApi.Security.Attributes;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/me")]
public sealed class MeController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [UserErasureMessages.ConfirmationInvalid] = StatusCodes.Status400BadRequest,
            [UserErasureMessages.UserNotFound] = StatusCodes.Status404NotFound,
            [PersonalDataExportMessages.NotFound] = StatusCodes.Status404NotFound,
            [PersonalDataExportMessages.Expired] = StatusCodes.Status404NotFound,
            [PersonalDataExportMessages.FileNotFound] = StatusCodes.Status404NotFound,
            [ProcessingConsentMessages.UnknownPurpose] = StatusCodes.Status400BadRequest,
            [ProcessingConsentMessages.VersionRequired] = StatusCodes.Status400BadRequest,
            [ProcessingConsentMessages.VersionNotCurrent] = StatusCodes.Status400BadRequest,
            [ProcessingConsentMessages.NotFound] = StatusCodes.Status404NotFound
        });

    private static readonly IReadOnlyDictionary<string, int> DataExportRequestStatuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [PersonalDataExportMessages.Accepted] = StatusCodes.Status202Accepted
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpGet]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<UserProfileOutput?>>> Get()
    {
        return await QueryAsync<GetMyProfileQuery, UserProfileOutput>(
            new GetMyProfileQuery());
    }

    [HttpPost("erasure")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<EraseUserCommandOutput?>>> Erase(
        [FromBody] EraseUserCommand command)
    {
        command.IsSelfService = true;

        return await SendAsync<
            EraseUserCommand,
            EraseUserCommandOutput>(command);
    }

    [HttpPost("data-export")]
    [RoleRequirement((int)HeimdallRoles.User, (int)HeimdallRoles.SystemAdmin)]
    [ProducesResponseType(typeof(DataOutput<RequestPersonalDataExportCommandOutput?>),
        StatusCodes.Status202Accepted)]
    public async Task<ActionResult<DataOutput<RequestPersonalDataExportCommandOutput?>>> RequestDataExport()
    {
        var command = new RequestPersonalDataExportCommand
        {
            CorrelationId = HttpContext.TraceIdentifier
        };

        return await SendAsync<
            RequestPersonalDataExportCommand,
            RequestPersonalDataExportCommandOutput>(command, DataExportRequestStatuses);
    }

    [HttpGet("data-export/{jobId:guid}")]
    [RoleRequirement((int)HeimdallRoles.User, (int)HeimdallRoles.SystemAdmin)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DataOutput<PersonalDataExportQueryOutput?>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDataExport(Guid jobId)
    {
        var result = await Queries.ExecuteQueryAsync<
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

        var response = Respond(result);

        return response.Result ?? Ok(response.Value);
    }

    [HttpGet("consents")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ProcessingConsentQueryOutput?>>> GetConsents()
    {
        return await QueryAsync<
            GetMyProcessingConsentsQuery,
            ProcessingConsentQueryOutput>(new GetMyProcessingConsentsQuery());
    }

    [HttpPost("consents")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<GrantProcessingConsentCommandOutput?>>> GrantConsent(
        [FromBody] GrantProcessingConsentCommand command)
    {
        return await SendAsync<
            GrantProcessingConsentCommand,
            GrantProcessingConsentCommandOutput>(command);
    }

    [HttpDelete("consents/{purpose}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<WithdrawProcessingConsentCommandOutput?>>> WithdrawConsent(
        string purpose)
    {
        return await SendAsync<
            WithdrawProcessingConsentCommand,
            WithdrawProcessingConsentCommandOutput>(new WithdrawProcessingConsentCommand
            {
                Purpose = purpose
            });
    }
}
