using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/attachments")]
public sealed class AttachmentsController : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [AttachmentMessages.AttachmentNotFound] = StatusCodes.Status404NotFound,
            [AttachmentMessages.StoredObjectNotFound] = StatusCodes.Status404NotFound,
            [AttachmentMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [AttachmentMessages.HardDeletedSuccessfully] = StatusCodes.Status200OK,
            [AttachmentMessages.HardDeleteRequiresSoftDeletion] = StatusCodes.Status409Conflict
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<IActionResult> Download(Guid id)
    {
        var result = await Queries.ExecuteQueryAsync<
            DownloadAttachmentQuery,
            DownloadAttachmentQueryOutput>(new DownloadAttachmentQuery { Id = id });
        if (!result.Success || result.Data is null)
        {
            var response = Respond(result);

            return response.Result ?? Ok(response.Value);
        }

        return File(
            result.Data.Content,
            result.Data.ContentType,
            result.Data.FileName,
            enableRangeProcessing: true);
    }

    [HttpDelete("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<AttachmentLifecycleCommandOutput?>>> Delete(
        Guid id)
    {
        return await SendAsync<
            DeleteAttachmentCommand,
            AttachmentLifecycleCommandOutput>(new DeleteAttachmentCommand { Id = id });
    }

    [HttpDelete("{id:guid}/hard")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<AttachmentLifecycleCommandOutput?>>> HardDelete(
        Guid id)
    {
        return await SendAsync<
            HardDeleteAttachmentCommand,
            AttachmentLifecycleCommandOutput>(new HardDeleteAttachmentCommand { Id = id });
    }
}
