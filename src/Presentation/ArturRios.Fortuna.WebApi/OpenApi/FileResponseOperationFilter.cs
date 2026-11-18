using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>
///     Publishes the file an action answers (<see cref="ProducesFileAttribute" />) as what it is on the wire:
///     one media type per content type the file can have, each <c>type: string, format: binary</c>, with the
///     headers <c>File(...)</c> sends beside it. API Explorer cannot say it:
///     <c>[ProducesResponseType(typeof(FileContentResult))]</c> listed the bytes under the JSON formatters'
///     media types (<c>application/json</c>, <c>text/json</c>, <c>text/plain</c>), and a status that answers
///     either a JSON envelope or a file (an export's record while it is pending, the export once it is ready)
///     keeps only one of the two types it is given.
/// </summary>
/// <remarks>
///     <para>
///         The file's media types go after any the status already lists, so a status that also answers the
///         JSON envelope keeps it first. A client generator picks <c>application/json</c> wherever it is
///         listed. The desktop native core shapes its responses by the schema of the first media type
///         (alphabetically) of the first success status that refers to one, which stays the envelope: every
///         file type sorts after <c>application/json</c>.
///     </para>
///     <para>
///         A file is always sent with <c>Content-Disposition: attachment</c> and its file name. A file sent
///         with range processing (<see cref="ProducesFileAttribute.Ranges" />) also carries
///         <c>Accept-Ranges: bytes</c>, and a request with a <c>Range</c> header gets <c>206 Partial
///         Content</c> with the requested bytes and a <c>Content-Range</c>, or <c>416 Range Not
///         Satisfiable</c> with no body and <c>Content-Range: bytes */length</c> when no requested range
///         falls inside the file.
///     </para>
/// </remarks>
public sealed class FileResponseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var file in context.MethodInfo.GetCustomAttributes<ProducesFileAttribute>())
        {
            operation.Responses ??= new OpenApiResponses();
            var response = Response(operation, file.StatusCode, context);
            if (file.Description is not null)
            {
                response.Description = file.Description;
            }

            AddFile(response, file);
            if (!file.Ranges)
            {
                continue;
            }

            var partial = Response(operation, StatusCodes.Status206PartialContent, context);
            partial.Description = "The requested range of the file, when the request has a Range header " +
                                  "and the file is what the status above sends.";
            AddFile(partial, file);
            Header(partial, HeaderNames.ContentRange,
                "The range sent and the file's length: `bytes <first>-<last>/<length>`.");

            var unsatisfiable = Response(operation, StatusCodes.Status416RangeNotSatisfiable, context);
            unsatisfiable.Description = "No range the Range header asks for falls inside the file. No body.";
            Header(unsatisfiable, HeaderNames.ContentRange, "The file's length: `bytes */<length>`.");
        }
    }

    private static OpenApiResponse Response(OpenApiOperation operation, int status, OperationFilterContext context)
    {
        var key = status.ToString(CultureInfo.InvariantCulture);
        if (!operation.Responses!.TryGetValue(key, out var published))
        {
            published = new OpenApiResponse { Description = ReasonPhrases.GetReasonPhrase(status) };
            operation.Responses[key] = published;
        }

        return published as OpenApiResponse ?? throw new InvalidOperationException(
            $"{context.MethodInfo.Name} documents a file for {key}, which refers to a shared response.");
    }

    private static void AddFile(OpenApiResponse response, ProducesFileAttribute file)
    {
        response.Content ??= new Dictionary<string, IOpenApiMediaType>();
        foreach (var contentType in file.ContentTypes)
        {
            response.Content[contentType] = new OpenApiMediaType
            {
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
            };
        }

        Header(response, HeaderNames.ContentDisposition,
            "Sent with the file: `attachment`, with its name as `filename` and `filename*`.");
        if (file.Ranges)
        {
            Header(response, HeaderNames.AcceptRanges, "Sent with the file: `bytes`.");
        }
    }

    private static void Header(OpenApiResponse response, string name, string description)
    {
        response.Headers ??= new Dictionary<string, IOpenApiHeader>();
        response.Headers[name] = new OpenApiHeader
        {
            Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        };
    }
}
