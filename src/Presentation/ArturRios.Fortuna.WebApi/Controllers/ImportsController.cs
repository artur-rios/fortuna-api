using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Mediator.Command;
using ArturRios.Output;
using ArturRios.Util.WebApi.AspNetCore;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/imports")]
public sealed class ImportsController(CommandMediator commandMediator) : Controller
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap =
        new Dictionary<string, int>
        {
            [ExcelImportMessages.Accepted] = StatusCodes.Status202Accepted,
            [ExcelImportMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [ExcelImportMessages.TargetNotFound] = StatusCodes.Status404NotFound,
            [ExcelImportMessages.TargetDeleted] = StatusCodes.Status409Conflict,
            [ExcelImportMessages.FileRequired] = StatusCodes.Status400BadRequest,
            [ExcelImportMessages.FileTooLarge] = StatusCodes.Status400BadRequest,
            [ExcelImportMessages.WorkbookInvalid] = StatusCodes.Status400BadRequest,
            [ExcelImportMessages.TargetTypeInvalid] = StatusCodes.Status400BadRequest,
            [ExcelImportMessages.DateColumnRequired] = StatusCodes.Status400BadRequest,
            [ExcelImportMessages.AmountColumnRequired] = StatusCodes.Status400BadRequest,
            [ExcelImportMessages.DirectionColumnRequired] = StatusCodes.Status400BadRequest,
            [ExcelImportMessages.ColumnsMustBeDistinct] = StatusCodes.Status400BadRequest,
            [PdfInvoiceImportMessages.Accepted] = StatusCodes.Status202Accepted,
            [PdfInvoiceImportMessages.ProfileNotFound] = StatusCodes.Status404NotFound,
            [PdfInvoiceImportMessages.CreditCardNotFound] = StatusCodes.Status404NotFound,
            [PdfInvoiceImportMessages.CreditCardDeleted] = StatusCodes.Status409Conflict,
            [PdfInvoiceImportMessages.FileRequired] = StatusCodes.Status400BadRequest,
            [PdfInvoiceImportMessages.FileTooLarge] = StatusCodes.Status400BadRequest
        };

    [HttpPost("excel")]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ImportExcelWorkbookCommandOutput?>>> Excel(
        [FromForm] ImportExcelWorkbookRequest request)
    {
        await using var stream = request.File?.OpenReadStream() ?? Stream.Null;
        using var content = new MemoryStream();
        await stream.CopyToAsync(content, HttpContext.RequestAborted);
        var command = new ImportExcelWorkbookCommand
        {
            TargetId = request.TargetId,
            TargetType = request.TargetType,
            FileName = request.File?.FileName ?? string.Empty,
            Content = content.ToArray(),
            Mapping = new ExcelColumnMapping(
                request.DateColumn,
                request.AmountColumn,
                request.DirectionColumn,
                request.DescriptionColumn,
                request.CategoryColumn,
                request.ExternalIdColumn),
            CreateMissingCategories = request.CreateMissingCategories,
            CorrelationId = HttpContext.TraceIdentifier
        };
        var result = await commandMediator.ExecuteCommandAsync<
            ImportExcelWorkbookCommand,
            ImportExcelWorkbookCommandOutput>(command);
        if (result.Errors?.Any(error => error.StartsWith("The mapped column '",
                StringComparison.Ordinal)) == true)
        {
            return BadRequest(result);
        }

        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }

    [HttpPost("pdf")]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ImportPdfInvoiceCommandOutput?>>> Pdf(
        [FromForm] ImportPdfInvoiceRequest request)
    {
        await using var stream = request.File?.OpenReadStream() ?? Stream.Null;
        using var content = new MemoryStream();
        await stream.CopyToAsync(content, HttpContext.RequestAborted);
        var command = new ImportPdfInvoiceCommand
        {
            CreditCardId = request.CreditCardId,
            FileName = request.File?.FileName ?? string.Empty,
            Content = content.ToArray(),
            CorrelationId = HttpContext.TraceIdentifier
        };
        var result = await commandMediator.ExecuteCommandAsync<
            ImportPdfInvoiceCommand,
            ImportPdfInvoiceCommandOutput>(command);
        return ResponseResolver.Resolve(result, statusMap: StatusMap);
    }
}

public sealed class ImportExcelWorkbookRequest
{
    public Guid TargetId { get; set; }
    public ImportTargetType TargetType { get; set; }
    public IFormFile? File { get; set; }
    public string DateColumn { get; set; } = string.Empty;
    public string AmountColumn { get; set; } = string.Empty;
    public string DirectionColumn { get; set; } = string.Empty;
    public string? DescriptionColumn { get; set; }
    public string? CategoryColumn { get; set; }
    public string? ExternalIdColumn { get; set; }
    public bool CreateMissingCategories { get; set; }
}

public sealed class ImportPdfInvoiceRequest
{
    public Guid CreditCardId { get; set; }
    public IFormFile? File { get; set; }
}
