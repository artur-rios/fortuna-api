using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>
///     Publishes the file an action answers (<see cref="ProducesFileAttribute" />) as what it is on the wire:
///     one media type per content type the file can have, each <c>type: string, format: binary</c>. API
///     Explorer cannot say it: <c>[ProducesResponseType(typeof(FileContentResult))]</c> listed the bytes under
///     the JSON formatters' media types (<c>application/json</c>, <c>text/json</c>, <c>text/plain</c>), and a
///     status that answers either a JSON envelope or a file (an export's record while it is pending, the
///     export once it is ready) keeps only one of the two types it is given.
/// </summary>
/// <remarks>
///     The file's media types go after any the status already lists, so a status that also answers the JSON
///     envelope keeps it first. A client generator picks <c>application/json</c> wherever it is listed. The
///     desktop native core shapes its responses by the schema of the first media type (alphabetically) of
///     the first success status that refers to one, which stays the envelope: every file type sorts after
///     <c>application/json</c>.
/// </remarks>
public sealed class FileResponseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var file in context.MethodInfo.GetCustomAttributes<ProducesFileAttribute>())
        {
            var status = file.StatusCode.ToString(CultureInfo.InvariantCulture);
            operation.Responses ??= new OpenApiResponses();
            if (!operation.Responses.TryGetValue(status, out var published))
            {
                published = new OpenApiResponse { Description = ReasonPhrases.GetReasonPhrase(file.StatusCode) };
                operation.Responses[status] = published;
            }

            if (published is not OpenApiResponse response)
            {
                throw new InvalidOperationException(
                    $"{context.MethodInfo.Name} documents a file for {status}, which refers to a shared response.");
            }

            if (file.Description is not null)
            {
                response.Description = file.Description;
            }

            response.Content ??= new Dictionary<string, IOpenApiMediaType>();
            foreach (var contentType in file.ContentTypes)
            {
                response.Content[contentType] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
                };
            }
        }
    }
}
