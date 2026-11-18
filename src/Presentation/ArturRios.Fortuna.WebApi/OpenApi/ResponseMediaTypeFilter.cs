using System.Globalization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>
///     Takes <c>text/plain</c> off every response whose body is not a string.
/// </summary>
/// <remarks>
///     API Explorer offers each output formatter's media types for every response, and MVC's
///     <c>StringOutputFormatter</c> claims <c>text/plain</c> for any type, so every envelope was documented as
///     <c>text/plain</c> beside <c>application/json</c> and <c>text/json</c>. At run time that formatter writes
///     only a <c>string</c>: an envelope asked for as <c>text/plain</c> is written by the JSON formatter as
///     <c>application/json</c>, so the document named a media type the API never sends. Nothing changes on the
///     wire; the two JSON media types stay. A response whose type is <c>string</c> keeps it.
/// </remarks>
public sealed class ResponseMediaTypeFilter : IOperationFilter
{
    private const string TextPlain = "text/plain";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var responseType in context.ApiDescription.SupportedResponseTypes)
        {
            if (responseType.Type == typeof(string) ||
                operation.Responses?.TryGetValue(
                    responseType.StatusCode.ToString(CultureInfo.InvariantCulture),
                    out var response) != true ||
                response is not OpenApiResponse { Content: { } content } concrete ||
                !content.ContainsKey(TextPlain))
            {
                continue;
            }

            // Rebuilt rather than removed from: a dictionary reuses the freed slot, so a media type added
            // later (a file's) would land first, ahead of the JSON ones.
            concrete.Content = content.Where(media => media.Key != TextPlain)
                .ToDictionary(media => media.Key, media => media.Value);
        }
    }
}
