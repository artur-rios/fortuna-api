using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Query;
using ArturRios.Mediator.Command;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/attachments")]
public sealed class AttachmentsController(
    QueryMediator queryMediator,
    CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [AttachmentMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [AttachmentMessages.AttachmentNotFound] = StatusCodes.Status404NotFound,
            [AttachmentMessages.StoredObjectNotFound] = StatusCodes.Status404NotFound,
            [AttachmentMessages.StorageUnavailable] = StatusCodes.Status503ServiceUnavailable,
            [AttachmentMessages.DeletedSuccessfully] = StatusCodes.Status200OK,
            [AttachmentMessages.HardDeletedSuccessfully] = StatusCodes.Status200OK,
            [AttachmentMessages.HardDeleteRequiresSoftDeletion] = StatusCodes.Status409Conflict
        };

    [HttpGet("{id:guid}")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<IActionResult> Download(Guid id)
    {
        var result = await queryMediator.ExecuteQueryAsync<
            DownloadAttachmentQuery,
            DownloadAttachmentQueryOutput>(new DownloadAttachmentQuery { Id = id });
        if (!result.Success || result.Data is null)
        {
            var response = ResponseResolver.Resolve(result, statusMap: StatusMap);
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
        var result = await commandMediator.ExecuteCommandAsync<
            DeleteAttachmentCommand,
            AttachmentLifecycleCommandOutput>(new DeleteAttachmentCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpDelete("{id:guid}/hard")]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<AttachmentLifecycleCommandOutput?>>> HardDelete(
        Guid id)
    {
        var result = await commandMediator.ExecuteCommandAsync<
            HardDeleteAttachmentCommand,
            AttachmentLifecycleCommandOutput>(new HardDeleteAttachmentCommand { Id = id });
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}
