using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/exports")]
public sealed class ExportsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [DataExportMessages.NotFound] = StatusCodes.Status404NotFound,
            [DataExportMessages.Expired] = StatusCodes.Status404NotFound,
            [DataExportMessages.FileNotFound] = StatusCodes.Status404NotFound
        });

    private static readonly IReadOnlyDictionary<string, int> RequestStatuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [DataExportMessages.Accepted] = StatusCodes.Status202Accepted
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost]
    [RoleRequirement((int)HeimdallRoles.User)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DataOutput<RequestDataExportCommandOutput?>),
        StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestExport([FromBody] RequestDataExportCommand command)
    {
        command.CorrelationId = HttpContext.TraceIdentifier;
        var result = await Commands.ExecuteCommandAsync<
            RequestDataExportCommand,
            RequestDataExportCommandOutput>(command);
        if (result.Success && result.Data?.Delivery == DataExportDelivery.Direct)
        {
            return File(
                result.Data.Content,
                result.Data.ContentType,
                result.Data.FileName);
        }

        var response = Respond(result, RequestStatuses);

        return response.Result ?? Ok(response.Value);
    }

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DataOutput<RetrieveDataExportQueryOutput?>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Retrieve(Guid id)
    {
        var result = await Queries.ExecuteQueryAsync<
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

        var response = Respond(result);

        return response.Result ?? Ok(response.Value);
    }
}
