using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Query.Input;
using ArturRios.Fortuna.Query.Output;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Query;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/attachments")]
public sealed class AttachmentsController(QueryMediator queryMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [AttachmentMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [AttachmentMessages.AttachmentNotFound] = StatusCodes.Status404NotFound,
            [AttachmentMessages.StoredObjectNotFound] = StatusCodes.Status404NotFound,
            [AttachmentMessages.StorageUnavailable] = StatusCodes.Status503ServiceUnavailable
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
}
