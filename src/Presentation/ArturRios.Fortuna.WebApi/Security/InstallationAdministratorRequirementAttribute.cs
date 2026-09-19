using ArturRios.Fortuna.Shared.Security;
using ArturRios.Util.WebApi.Security.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
/// Restricts an action to whoever administers the installation: a Heimdall system administrator,
/// or the owner of an offline installation signed in with its local account. It is the
/// controller-level counterpart of <see cref="InstallationAdministration"/>, which handlers check
/// again as defense in depth.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InstallationAdministratorRequirementAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var identity = context.HttpContext.GetUser<FortunaIdentity>();
        if (identity is null)
        {
            context.Result = new UnauthorizedResult();

            return;
        }

        if (!InstallationAdministration.IsAdministrator(identity.RoleId, identity.IsLocal))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
