namespace ArturRios.Fortuna.WebApi.Security;

/// <summary>
/// Bridges ASP.NET Core's fully validated JWT principal to the identity shape used by
/// Fortuna handlers and the shared ArturRios authorization filters.
/// </summary>
public sealed class AuthenticatedActorMiddleware(
    RequestDelegate next,
    FortunaIdentityMapper mapper)
{
    private const string AuthenticatedUserItemKey = "User";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claims = context.User.Claims
                .GroupBy(claim => claim.Type, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
            var identity = mapper.FromClaims(claims);

            if (identity is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Items[AuthenticatedUserItemKey] = identity;
        }

        await next(context);
    }
}
