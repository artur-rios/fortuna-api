using ArturRios.Fortuna.Domain.Security;
using ArturRios.Fortuna.Shared.Security;
using ArturRios.Jwt;

namespace ArturRios.Fortuna.WebApi.Security;

public sealed class LocalAuthTokenIssuer(
    JwtHandler jwtHandler,
    FortunaIdentityMapper mapper,
    Configuration.FortunaOptions options,
    TimeProvider timeProvider) : ILocalAuthTokenIssuer
{
    public LocalAuthToken Issue(Guid subject, string displayName)
    {
        var identity = new FortunaIdentity(subject, (int)HeimdallRoles.User, null, [])
        {
            DisplayName = displayName,
            IsLocal = true
        };
        var token = jwtHandler.CreateToken(new JwtConfiguration(
            options.AuthTokenExpirationInSeconds,
            options.AuthTokenIssuer,
            options.AuthTokenAudience,
            options.AuthTokenSecret,
            mapper.ToClaims(identity)));

        return new LocalAuthToken(
            token,
            timeProvider.GetUtcNow().AddSeconds(options.AuthTokenExpirationInSeconds));
    }
}
