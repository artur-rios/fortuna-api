using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using Microsoft.AspNetCore.Mvc;

namespace ArturRios.Fortuna.WebApi.Output;

/// <summary>
/// Shapes failures raised outside the handlers (model binding, unhandled exceptions) as the same
/// <see cref="DataOutput{T}"/> envelope every endpoint returns, instead of ProblemDetails or an
/// empty body.
/// </summary>
public static class ApiErrorResponses
{
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
    /// Terminal handler for unhandled exceptions: a 500 with a generic message. The exception
    /// itself is logged by the exception-handler middleware and never reaches the caller.
    /// </summary>
    public static Task WriteUnexpectedErrorAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return context.Response.WriteAsJsonAsync(
            DataOutput<object?>.New.WithError(RequestMessages.UnexpectedError),
            context.RequestAborted);
    }

    private static string Describe(string key, string message)
    {
        var text = string.IsNullOrWhiteSpace(message) ? RequestMessages.Invalid : message;

        return string.IsNullOrWhiteSpace(key) ? text : $"{key}: {text}";
    }
}
