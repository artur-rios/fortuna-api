using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
///     Ensures every authenticated Heimdall subject has one local Fortuna profile. A subject known
///     to be provisioned is not looked up again until its cache entry expires or it is erased.
/// </summary>
public sealed class UserProfileProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IRequestActorAccessor actorAccessor,
        IUserProfileProvisioner profiles,
        IProvisionedProfileCache provisioned)
    {
        var actor = actorAccessor.Actor;
        // Erasure must only operate on an existing Fortuna account. Provisioning here would
        // recreate an erased profile on a repeated request before the handler can return 404.
        // Routing ignores case and a trailing slash, so the check must too.
        var isErasureRequest = IsErasurePath(context.Request.Path);
        if (actor is not null && !actor.IsLocal && !isErasureRequest)
        {
            if (string.IsNullOrWhiteSpace(actor.DisplayName))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                return;
            }

            if (!provisioned.IsProvisioned(actor.SubjectId))
            {
                await profiles.GetOrCreateAsync(
                    actor.SubjectId,
                    actor.DisplayName,
                    context.RequestAborted);
                provisioned.MarkProvisioned(actor.SubjectId);
            }
        }

        await next(context);
    }

    public static bool IsErasurePath(PathString path) =>
        path.StartsWithSegments(ErasurePath, StringComparison.OrdinalIgnoreCase, out var remaining) &&
        (!remaining.HasValue || remaining.Value == "/");

    private static readonly PathString ErasurePath = new("/api/me/erasure");
}
