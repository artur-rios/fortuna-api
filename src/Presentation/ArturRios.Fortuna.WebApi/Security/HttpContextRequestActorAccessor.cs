using ArturRios.Fortuna.Shared.Security;
using ArturRios.Util.WebApi.Security.Extensions;

namespace ArturRios.Fortuna.WebApi.Security;

public sealed class HttpContextRequestActorAccessor(IHttpContextAccessor httpContextAccessor)
    : IRequestActorAccessor
{
    public RequestActor? Actor
    {
        get
        {
            var identity = httpContextAccessor.HttpContext?.GetUser<FortunaIdentity>();

            return identity is null
                ? null
                : new RequestActor(
                    identity.SubjectId,
                    identity.RoleId,
                    identity.ScopeId,
                    identity.Permissions)
                {
                    DisplayName = identity.DisplayName
                };
        }
    }
}
