using ArturRios.Fortuna.Shared.Attachments;
using ArturRios.Fortuna.Shared.Ingestion;
using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Fortuna.WebApi.Filters;
using ArturRios.Fortuna.WebApi.Requests;
using ArturRios.Output;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace ArturRios.Fortuna.WebApi.Tests;

public sealed class UploadLimitUnitTests
{
    private static readonly UploadLimits Limits = new(
        new AttachmentOptions(1_000, ["application/pdf"]),
        new ExcelImportOptions(2_000),
        new PdfInvoiceImportOptions(3_000));

    [UnitTheory]
    [InlineData(UploadKind.Attachment, 1_000)]
    [InlineData(UploadKind.ExcelImport, 2_000)]
    [InlineData(UploadKind.PdfInvoiceImport, 3_000)]
    public void GivenUploadKind_WhenLimitsResolved_ThenConfiguredOptionIsUsed(
        UploadKind kind,
        long expected)
    {
        Assert.Equal(expected, Limits.MaximumFileBytes(kind));
        Assert.Equal(expected + UploadLimits.MultipartOverheadBytes, Limits.MaximumRequestBytes(kind));
    }

    [UnitFact]
    public void GivenDeclaredLengthOverLimit_WhenFiltered_ThenDataOutputErrorIsReturned()
    {
        var context = Context(contentLength: 2_000 + UploadLimits.MultipartOverheadBytes + 1);

        new UploadLimitFilter(UploadKind.ExcelImport, Limits).OnResourceExecuting(context);

        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        var output = Assert.IsType<DataOutput<object?>>(result.Value);
        Assert.Equal([ExcelImportMessages.FileTooLarge], output.Errors);
    }

    [UnitFact]
    public void GivenRequestWithinLimit_WhenFiltered_ThenBodyAndFormLimitsFollowTheOption()
    {
        var bodySize = new BodySizeFeature();
        var context = Context(contentLength: 100, bodySize);

        new UploadLimitFilter(UploadKind.PdfInvoiceImport, Limits).OnResourceExecuting(context);

        Assert.Null(context.Result);
        Assert.Equal(3_000 + UploadLimits.MultipartOverheadBytes, bodySize.MaxRequestBodySize);
        Assert.NotNull(context.HttpContext.Features.Get<IFormFeature>());
    }

    [UnitFact]
    public async Task GivenFileLongerThanLimit_WhenRead_ThenItIsRefusedWithoutReading()
    {
        var stream = new ThrowingStream();
        var file = new FormFile(stream, 0, 11, "File", "large.xlsx");

        var uploaded = await UploadedFile.ReadAsync(file, 10, CancellationToken.None);

        Assert.True(uploaded.TooLarge);
        Assert.Empty(uploaded.Content);
    }

    [UnitFact]
    public async Task GivenFileWithinLimit_WhenRead_ThenContentIsReadOnce()
    {
        var bytes = "%PDF-1.7"u8.ToArray();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "File", "a.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var uploaded = await UploadedFile.ReadAsync(file, bytes.Length, CancellationToken.None);

        Assert.False(uploaded.TooLarge);
        Assert.Equal(bytes, uploaded.Content);
        Assert.Equal("a.pdf", uploaded.FileName);
        Assert.Equal("application/pdf", uploaded.ContentType);
    }

    private static ResourceExecutingContext Context(
        long contentLength,
        IHttpMaxRequestBodySizeFeature? bodySize = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentLength = contentLength;
        httpContext.Request.ContentType = "multipart/form-data; boundary=x";
        if (bodySize is not null)
        {
            httpContext.Features.Set(bodySize);
        }

        return new ResourceExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            [],
            []);
    }

    private sealed class BodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("The file must not be read.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The file must not be read.");
    }
}
