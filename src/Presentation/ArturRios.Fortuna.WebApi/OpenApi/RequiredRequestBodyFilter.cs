using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArturRios.Fortuna.WebApi.OpenApi;

/// <summary>
///     Marks a JSON request body required when model binding rejects a request without one. MVC already
///     decides that for each <c>[FromBody]</c> parameter (an empty body is a 400 unless the parameter is
///     nullable or has a default, which lets it be omitted) and API Explorer reports it as the parameter's
///     <see cref="Microsoft.AspNetCore.Mvc.ApiExplorer.ApiParameterDescription.IsRequired" />; Swashbuckle
///     reads only <c>[Required]</c> on a controller's parameters, so without this every body was optional.
/// </summary>
public sealed class RequiredRequestBodyFilter : IRequestBodyFilter
{
    public void Apply(IOpenApiRequestBody requestBody, RequestBodyFilterContext context)
    {
        if (context.BodyParameterDescription is { IsRequired: true } &&
            requestBody is OpenApiRequestBody body)
        {
            body.Required = true;
        }
    }
}
