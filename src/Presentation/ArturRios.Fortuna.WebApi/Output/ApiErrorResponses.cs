using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Output;

/// <summary>
/// Shapes failures raised outside the handlers (model binding, unhandled exceptions) as the same
/// <see cref="DataOutput{T}"/> envelope every endpoint returns, instead of ProblemDetails or an
/// empty body.
/// </summary>
public static class ApiErrorResponses
{
    private const string LoggerCategory = "ArturRios.Fortuna.WebApi.Output.ApiErrorResponses";

    /// <summary>
    /// Replaces the default ProblemDetails for a body that cannot be bound (malformed JSON, a value
    /// of the wrong type) with a 400 whose errors name each offending field.
    /// </summary>
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error => Describe(entry.Key, error.ErrorMessage)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new BadRequestObjectResult(DataOutput<object?>.New.WithErrors(
            errors.Length == 0 ? [RequestMessages.Invalid] : errors));
    }

    /// <summary>
    /// Terminal handler for unhandled exceptions. A request Kestrel refused as the API read it (a
    /// <see cref="BadHttpRequestException" />: a body over the size limit, too slow, malformed or cut
    /// short) is the client's fault: it is answered with the status Kestrel gave it (413, 408, 400)
    /// and a message saying which, and logged as a warning. Anything else is a 500 with a generic
    /// message. Either way the exception itself never reaches the caller.
    /// </summary>
    public static Task WriteErrorAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var refusal = ClientFault(exception);
        if (refusal is { } fault)
        {
            // A client mistake, not a server failure: a warning, with Kestrel's reason and no stack trace.
            context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory).LogWarning(
                "Refused a request Kestrel could not read with {StatusCode}: {Reason}",
                fault.Status,
                exception!.Message);
        }

        context.Response.StatusCode = refusal?.Status ?? StatusCodes.Status500InternalServerError;

        return context.Response.WriteAsJsonAsync(
            DataOutput<object?>.New.WithError(refusal?.Message ?? RequestMessages.UnexpectedError),
            context.RequestAborted);
    }

    /// <summary>
    /// Whether <paramref name="exception" /> is a request Kestrel refused (a client error), which the
    /// exception handler answers with its own status and does not report as an unhandled exception.
    /// </summary>
    public static bool IsClientFault(Exception? exception) => ClientFault(exception) is not null;

    /// <summary>
    /// The status and message a request Kestrel refused is answered with: 413 for a body over the
    /// size limit, 408 for one that arrived too slowly, 400 (or Kestrel's other 4xx) for one it could
    /// not read. <see langword="null" /> for any other exception.
    /// </summary>
    public static (int Status, string Message)? ClientFault(Exception? exception) =>
        exception is BadHttpRequestException { StatusCode: >= 400 and < 500 } refused
            ? (refused.StatusCode, refused.StatusCode switch
            {
                StatusCodes.Status413PayloadTooLarge => RequestMessages.BodyTooLarge,
                StatusCodes.Status408RequestTimeout => RequestMessages.BodyTooSlow,
                _ => RequestMessages.Unreadable
            })
            : null;

    private static string Describe(string key, string message)
    {
        var text = string.IsNullOrWhiteSpace(message) ? RequestMessages.Invalid : message;

        return string.IsNullOrWhiteSpace(key) ? text : $"{key}: {text}";
    }
}
