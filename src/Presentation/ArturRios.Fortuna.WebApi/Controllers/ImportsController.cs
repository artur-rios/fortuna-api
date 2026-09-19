using ArturRios.Fortuna.Command.Input;
using ArturRios.Fortuna.Command.Output;
using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Filters;
using ArturRios.Fortuna.WebApi.Requests;
using ArturRios.Output;
using ArturRios.Util.WebApi.Security.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Controllers;

[ApiController]
[Route("api/imports")]
public sealed class ImportsController(
    UploadLimits uploadLimits) : FortunaController
{
    private static readonly IReadOnlyDictionary<string, int> Statuses =
        FortunaStatusMap.With(new Dictionary<string, int>
        {
            [ExcelImportMessages.Accepted] = StatusCodes.Status202Accepted,
            [ExcelImportMessages.TargetIdRequired] = StatusCodes.Status400BadRequest,
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
            [PdfInvoiceImportMessages.CreditCardIdRequired] = StatusCodes.Status400BadRequest,
            [PdfInvoiceImportMessages.CreditCardNotFound] = StatusCodes.Status404NotFound,
            [PdfInvoiceImportMessages.CreditCardDeleted] = StatusCodes.Status409Conflict,
            [PdfInvoiceImportMessages.FileRequired] = StatusCodes.Status400BadRequest,
            [PdfInvoiceImportMessages.FileTooLarge] = StatusCodes.Status400BadRequest,
            [PdfInvoiceImportMessages.FileInvalid] = StatusCodes.Status400BadRequest,
            [PdfInvoiceImportMessages.FileNameTooLong] = StatusCodes.Status400BadRequest,
            [ExcelImportMessages.FileNameTooLong] = StatusCodes.Status400BadRequest
        });

    protected override IReadOnlyDictionary<string, int> StatusMap => Statuses;

    [HttpPost("excel")]
    [Consumes("multipart/form-data")]
    [UploadLimit(UploadKind.ExcelImport)]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ImportExcelWorkbookCommandOutput?>>> Excel(
        [FromForm] ImportExcelWorkbookRequest request)
    {
        var file = await UploadedFile.ReadAsync(
            request.File,
            uploadLimits.MaximumFileBytes(UploadKind.ExcelImport),
            HttpContext.RequestAborted);
        if (file.TooLarge)
        {
            return BadRequest(DataOutput<ImportExcelWorkbookCommandOutput?>.New
                .WithError(ExcelImportMessages.FileTooLarge));
        }

        var command = new ImportExcelWorkbookCommand
        {
            TargetId = request.TargetId,
            TargetType = request.TargetType,
            FileName = file.FileName,
            Content = file.Content,
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

        return await SendAsync<
            ImportExcelWorkbookCommand,
            ImportExcelWorkbookCommandOutput>(command);
    }

    [HttpPost("pdf")]
    [Consumes("multipart/form-data")]
    [UploadLimit(UploadKind.PdfInvoiceImport)]
    [RoleRequirement((int)HeimdallRoles.User)]
    public async Task<ActionResult<DataOutput<ImportPdfInvoiceCommandOutput?>>> Pdf(
        [FromForm] ImportPdfInvoiceRequest request)
    {
        var file = await UploadedFile.ReadAsync(
            request.File,
            uploadLimits.MaximumFileBytes(UploadKind.PdfInvoiceImport),
            HttpContext.RequestAborted);
        if (file.TooLarge)
        {
            return BadRequest(DataOutput<ImportPdfInvoiceCommandOutput?>.New
                .WithError(PdfInvoiceImportMessages.FileTooLarge));
        }

        var command = new ImportPdfInvoiceCommand
        {
            CreditCardId = request.CreditCardId,
            FileName = file.FileName,
            Content = file.Content,
            CorrelationId = HttpContext.TraceIdentifier
        };

        return await SendAsync<
            ImportPdfInvoiceCommand,
            ImportPdfInvoiceCommandOutput>(command);
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
