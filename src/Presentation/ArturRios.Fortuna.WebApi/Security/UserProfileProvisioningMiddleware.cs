using ArturRios.Fortuna.Shared.Security;
using ArturRios.Fortuna.Shared.Users;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>Ensures every authenticated Heimdall subject has one local Fortuna profile.</summary>
public sealed class UserProfileProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IRequestActorAccessor actorAccessor,
        IUserProfileProvisioner profiles)
    {
        var actor = actorAccessor.Actor;
        if (actor is not null)
        {
            if (string.IsNullOrWhiteSpace(actor.DisplayName))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await profiles.GetOrCreateAsync(
                actor.SubjectId,
                actor.DisplayName,
                context.RequestAborted);
        }

        await next(context);
    }
}
