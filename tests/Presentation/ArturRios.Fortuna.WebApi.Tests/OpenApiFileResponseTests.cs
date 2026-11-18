using System.Net.Http.Headers;
using System.Reflection;
using ArturRios.Fortuna.Domain.Exports;
using ArturRios.Fortuna.Integration.Exports;
using ArturRios.Fortuna.Shared.Exports;
using ArturRios.Fortuna.WebApi.Controllers;
using ArturRios.Fortuna.WebApi.OpenApi;
using ArturRios.Util.Test.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Tests;

/// <summary>
///     Keeps the routes that answer a file honest in docs/openapi/fortuna.json: the file is listed under the
///     media types it is really sent as, each with a binary schema, and never under the JSON formatters'
///     media types, which is how a client generator tells bytes from JSON.
/// </summary>
public sealed class OpenApiFileResponseTests
{
    private const string Excel = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // What the JSON output formatters write, in the order the document lists them. Never raw bytes.
    private static readonly string[] JsonMediaTypes = ["application/json", "text/json"];

    [UnitTheory]
    [InlineData("POST /api/exports", new[] { Excel, "application/pdf", "text/csv" })]
    [InlineData("GET /api/exports/{id}", new[] { Excel, "application/pdf", "text/csv" })]
    [InlineData("GET /api/me/data-export/{jobId}", new[] { "application/zip" })]
    [InlineData("GET /api/attachments/{id}", new[] { "application/pdf", "image/jpeg", "image/png" })]
    public void GivenFileRoute_WhenDocumented_ThenItsSuccessListsEachMediaTypeOfTheFileAsBinary(
        string operation,
        string[] mediaTypes)
    {
        var files = CommittedOpenApiDocument.Content(operation, 200)
            .Where(media => CommittedOpenApiDocument.IsBinary(media.Value))
            .Select(media => media.Key);

        // In this order: a generator that reads the first media type decodes text/csv as a string.
        Assert.Equal(mediaTypes, files);
    }

    [UnitTheory]
    [InlineData("GET /api/exports/{id}")]
    [InlineData("GET /api/me/data-export/{jobId}")]
    [InlineData("GET /api/attachments/{id}")]
    public void GivenRangedFileRoute_WhenDocumented_ThenItListsThePartialAndUnsatisfiableAnswersToARange(
        string operation)
    {
        var files = CommittedOpenApiDocument.Content(operation, 200)
            .Where(media => CommittedOpenApiDocument.IsBinary(media.Value))
            .Select(media => media.Key)
            .ToArray();
        var partial = CommittedOpenApiDocument.Content(operation, 206);

        // File(..., enableRangeProcessing: true): Accept-Ranges on the file, the requested bytes with a
        // Content-Range, and 416 with no body and `bytes */length` when no range fits.
        Assert.Equal(files, partial.Select(media => media.Key));
        Assert.All(partial, media => Assert.True(CommittedOpenApiDocument.IsBinary(media.Value)));
        Assert.Equal(["Accept-Ranges", "Content-Disposition"],
            CommittedOpenApiDocument.Headers(operation, 200).Order(StringComparer.Ordinal));
        Assert.Equal(["Accept-Ranges", "Content-Disposition", "Content-Range"],
            CommittedOpenApiDocument.Headers(operation, 206).Order(StringComparer.Ordinal));
        Assert.Empty(CommittedOpenApiDocument.Content(operation, 416));
        Assert.Equal(["Content-Range"], CommittedOpenApiDocument.Headers(operation, 416));
    }

    [UnitFact]
    public void GivenTheDirectExport_WhenDocumented_ThenItsFileCarriesItsNameAndNoRangeAnswers()
    {
        using var contract = CommittedOpenApiDocument.Load();
        var responses = contract.RootElement.GetProperty("paths").GetProperty("/api/exports")
            .GetProperty("post").GetProperty("responses");

        // File(content, type, name) without range processing: Content-Disposition, nothing about ranges.
        Assert.Equal(["Content-Disposition"], CommittedOpenApiDocument.Headers("POST /api/exports", 200));
        Assert.False(responses.TryGetProperty("206", out _));
        Assert.False(responses.TryGetProperty("416", out _));
    }

    [UnitTheory]
    [InlineData(DataExportFormat.Csv)]
    [InlineData(DataExportFormat.Excel)]
    [InlineData(DataExportFormat.Pdf)]
    public void GivenExportFormat_WhenRendered_ThenBothExportRoutesDocumentTheMediaTypeItIsSentAs(
        DataExportFormat format)
    {
        var rendered = new DataExportRenderer().Render(
            new DataExportDocument("transactions", [], [], [], "pt-BR"), format);
        var mediaType = MediaTypeHeaderValue.Parse(rendered.ContentType).MediaType!;

        Assert.True(CommittedOpenApiDocument.DocumentsFile("POST /api/exports", 200, mediaType),
            $"POST /api/exports sends a {format} export as {mediaType}, which its 200 does not list as binary.");
        Assert.True(CommittedOpenApiDocument.DocumentsFile("GET /api/exports/{id}", 200, mediaType),
            $"GET /api/exports/{{id}} sends a {format} export as {mediaType}, which its 200 does not list as binary.");
    }

    [UnitTheory]
    [InlineData("GET /api/exports/{id}", "RetrieveDataExportQueryOutputDataOutput")]
    [InlineData("GET /api/me/data-export/{jobId}", "PersonalDataExportQueryOutputDataOutput")]
    public void GivenRouteAnsweringTheEnvelopeOrAFile_WhenDocumented_ThenTheEnvelopeStaysItsJsonResponse(
        string operation,
        string envelope)
    {
        var content = CommittedOpenApiDocument.Content(operation, 200);

        // Listed first, as before the file was documented (text/plain, never sent, is gone): swagger_parser
        // picks application/json wherever it is, and the native core shapes the response by the
        // alphabetically first media type's schema.
        Assert.Equal(JsonMediaTypes, content.Take(JsonMediaTypes.Length).Select(media => media.Key));
        Assert.All(content.Take(JsonMediaTypes.Length), media =>
            Assert.Equal($"#/components/schemas/{envelope}", media.Value.GetProperty("$ref").GetString()));
        Assert.Equal("application/json", content.Select(media => media.Key).Order(StringComparer.Ordinal).First());
    }

    [UnitFact]
    public void GivenEveryResponse_WhenDocumented_ThenNoJsonMediaTypeCarriesRawBytes()
    {
        using var contract = CommittedOpenApiDocument.Load();
        var checkedMediaTypes = 0;

        foreach (var path in contract.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                foreach (var response in operation.Value.GetProperty("responses").EnumerateObject())
                {
                    if (!response.Value.TryGetProperty("content", out var content))
                    {
                        continue;
                    }

                    foreach (var media in content.EnumerateObject().Where(media => JsonMediaTypes.Contains(media.Name)))
                    {
                        Assert.False(CommittedOpenApiDocument.IsBinary(media.Value.GetProperty("schema")),
                            $"{operation.Name.ToUpperInvariant()} {path.Name} {response.Name} lists raw bytes " +
                            $"as {media.Name}.");
                        checkedMediaTypes++;
                    }
                }
            }
        }

        Assert.True(checkedMediaTypes > 300, $"Only {checkedMediaTypes} JSON media types were checked.");
    }

    [UnitFact]
    public void GivenActionsThatMayAnswerAFile_WhenDeclared_ThenEachDocumentsItsFile()
    {
        // Every other Fortuna action returns ActionResult<DataOutput<...>>, which API Explorer documents. An
        // IActionResult action answers File(...) on success, which only [ProducesFile] documents.
        var actions = typeof(FortunaController).Assembly.GetExportedTypes()
            .Where(type => type is { IsAbstract: false } && type.IsSubclassOf(typeof(FortunaController)))
            .SelectMany(type =>
                type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(action => action.ReturnType == typeof(Task<IActionResult>))
            .ToArray();

        Assert.Equal(
            ["AttachmentsController.Download", "ExportsController.RequestExport", "ExportsController.Retrieve",
                "MeController.GetDataExport"],
            actions.Select(action => $"{action.DeclaringType!.Name}.{action.Name}").Order(StringComparer.Ordinal));
        Assert.All(actions, action =>
            Assert.NotNull(action.GetCustomAttribute<ProducesFileAttribute>()));
    }
}
