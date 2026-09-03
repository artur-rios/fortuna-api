namespace ArturRios.Fortuna.Shared.Security;

public interface ILocalAuthTokenIssuer
{
    LocalAuthToken Issue(Guid subject, string displayName);
}

public sealed record LocalAuthToken(string Token, DateTimeOffset ExpiresAt);
