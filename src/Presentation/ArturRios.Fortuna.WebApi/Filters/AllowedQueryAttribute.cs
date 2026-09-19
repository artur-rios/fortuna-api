using ArturRios.Fortuna.Shared.Messages;
using ArturRios.Output;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ArturRios.Fortuna.WebApi.Filters;

/// <summary>
///     Rejects a request carrying a query parameter the action does not bind, so a misspelled or
///     unsupported filter is reported as a 400 instead of being silently ignored. Names compare
///     case-insensitively, like model binding.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowedQueryAttribute(params string[] parameters) : ActionFilterAttribute
{
    private readonly HashSet<string> allowed = new(parameters, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Parameters => allowed;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var unsupported = context.HttpContext.Request.Query.Keys
            .FirstOrDefault(key => !allowed.Contains(key));
        if (unsupported is null)
        {
            return;
        }

        var error = QueryParameterMessages.Unsupported(unsupported);
        context.Result = new BadRequestObjectResult(IsPaginated(context.ActionDescriptor)
            ? PaginatedOutput<object>.New.WithError(error)
            : DataOutput<object?>.New.WithError(error));
    }

    // Keeps the envelope shape the action itself returns: a page or a single payload.
    private static bool IsPaginated(ActionDescriptor action) =>
        action is ControllerActionDescriptor controllerAction &&
        UnwrapResult(controllerAction.MethodInfo.ReturnType) is { IsGenericType: true } payload &&
        payload.GetGenericTypeDefinition() == typeof(PaginatedOutput<>);

    private static Type UnwrapResult(Type type)
    {
        while (type.IsGenericType &&
               (type.GetGenericTypeDefinition() == typeof(Task<>) ||
                type.GetGenericTypeDefinition() == typeof(ActionResult<>)))
        {
            type = type.GetGenericArguments()[0];
        }

        return type;
    }
}
