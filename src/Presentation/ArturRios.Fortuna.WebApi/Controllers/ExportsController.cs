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
[Route("api/exports")]
public sealed class ExportsController(
    CommandMediator commandMediator,
    QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [DataExportMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [DataExportMessages.NotFound] = StatusCodes.Status404NotFound,
            [DataExportMessages.Expired] = StatusCodes.Status404NotFound,
            [DataExportMessages.FileNotFound] = StatusCodes.Status404NotFound,
            [DataExportMessages.StorageUnavailable] = StatusCodes.Status503ServiceUnavailable
        };

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DataOutput<RequestDataExportCommandOutput?>),
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestExport([FromBody] RequestDataExportCommand command)
    {
        command.CorrelationId = HttpContext.TraceIdentifier;
        var result = await commandMediator.ExecuteCommandAsync<
            RequestDataExportCommand,
            RequestDataExportCommandOutput>(command);
        if (result.Success && result.Data?.Delivery == DataExportDelivery.Direct)
        {
            return File(
                result.Data.Content,
                result.Data.ContentType,
                result.Data.FileName);
        }

        if (result.Errors?.Count > 0 &&
            !result.Errors.Contains(DataExportMessages.ProfileNotFound))
        {
            return BadRequest(result);
        }

        var response = ResponseResolver.Resolve(result, statusMap: new Dictionary<string, int>
        {
            [DataExportMessages.Accepted] = StatusCodes.Status202Accepted,
            [DataExportMessages.ProfileNotFound] = StatusCodes.Status404NotFound
        });
        return response.Result ?? Ok(response.Value);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DataOutput<RetrieveDataExportQueryOutput?>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Retrieve(Guid id)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            GetDataExportQuery,
            RetrieveDataExportQueryOutput>(new GetDataExportQuery { Id = id });
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
