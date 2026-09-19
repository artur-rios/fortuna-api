using ArturRios.Fortuna.WebApi.Requests;
using ArturRios.Output;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ArturRios.Fortuna.WebApi.Filters;

/// <summary>
///     Sizes an upload endpoint's request limits from the configured option instead of a
///     hard-coded constant: the Kestrel body limit and the multipart form limit both follow it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UploadLimitAttribute : TypeFilterAttribute
{
    public UploadLimitAttribute(UploadKind kind)
        : base(typeof(UploadLimitFilter))
    {
        Arguments = [kind];
    }
}

/// <summary>
///     Runs before model binding reads the body. A request whose declared length already exceeds
///     the limit is refused with the endpoint's DataOutput error rather than a bare 413 from the
///     server; for the rest the per-request body and form limits are raised or lowered to match.
/// </summary>
public sealed class UploadLimitFilter(UploadKind kind, UploadLimits limits) : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var maximumRequestBytes = limits.MaximumRequestBytes(kind);
        var httpContext = context.HttpContext;
        if (httpContext.Request.ContentLength > maximumRequestBytes)
        {
            context.Result = new BadRequestObjectResult(
                DataOutput<object?>.New.WithError(limits.FileTooLarge(kind)));

            return;
        }

        var bodySize = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
        {
            bodySize.MaxRequestBodySize = maximumRequestBytes;
        }

        var form = httpContext.Features.Get<IFormFeature>();
        if (form?.Form is null && httpContext.Request.HasFormContentType)
        {
            httpContext.Features.Set<IFormFeature>(new FormFeature(
                httpContext.Request,
                new FormOptions { MultipartBodyLengthLimit = maximumRequestBytes }));
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
